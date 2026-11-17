"""Run one inspection for the queue consumer (`messaging/inspections.py`), as app_ai.

The consumer calls `InspectionWorker.inspect` once per delivery of an InspectionRequested, with
the tenant of the message's envelope and nothing else. The checkpoints (`checkpoints.py`), the
regulation text and the evidence search all run inside `tenant_transaction` for that tenant, and
the evidence comes only from the message's establishment (`search_establishment`).

A delivery of an inspection that already finished returns its stored result again, with nothing
run or published, so the consumer can publish it once more. Otherwise the graph runs, or resumes
from its last checkpoint without judging finished items again (`graph.py`).
"""

import functools
import logging
import time
from collections.abc import Awaitable, Callable
from uuid import UUID

import anyio
from sqlalchemy import Engine, text

from normetry_ai.db import tenant_transaction
from normetry_ai.inspection import graph
from normetry_ai.inspection.checklist import ChecklistItem
from normetry_ai.inspection.checkpoints import TenantCheckpointSaver
from normetry_ai.inspection.judge import JudgeModel, evidence_query, regulation_chunks
from normetry_ai.llm.embeddings import Embedder
from normetry_ai.llm.reranker import Reranker
from normetry_ai.llm.usage import record_usage
from normetry_ai.messaging.contracts import (
    Finding,
    InspectionCompleted,
    InspectionFailed,
    InspectionProgressed,
    InspectionRequested,
    InspectionStarted,
    Payload,
)
from normetry_ai.retrieval.establishment import search_establishment
from normetry_ai.retrieval.models import RetrievedChunk
from normetry_ai.settings import AnswerSettings, InspectionSettings, RetrievalSettings

logger = logging.getLogger(__name__)

# Indexed, not deleted: a document the evidence search can return.
_HAS_DOCUMENTS = text(
    """
    SELECT EXISTS (SELECT FROM ai.tenant_documents
                   WHERE establishment_id = :establishment_id AND deleted_at IS NULL
                     AND indexed_version IS NOT NULL)
    """
)
NO_DOCUMENTS = "The establishment has no indexed document to use as evidence."
BUDGET_EXCEEDED = "The inspection stopped before a model call that could pass its token ceiling."


class InspectionMismatch(ValueError):
    """The inspection's checkpoints were started for another establishment, jurisdiction or type:
    neither resumed nor published again, since they would mix or report another's evidence."""


class InspectionWorker:
    """Runs inspections; shares the service's embedder and re-ranker with the chat and ingestion."""

    def __init__(
        self,
        engine: Engine,
        embedder: Embedder,
        reranker: Reranker,
        model: JudgeModel,
        *,
        answers: AnswerSettings,
        retrieval: RetrievalSettings,
        inspection: InspectionSettings,
    ) -> None:
        self.engine, self.embedder, self.reranker, self.model = engine, embedder, reranker, model
        self.answers, self.retrieval, self.inspection = answers, retrieval, inspection

    def reachable(self) -> bool:
        """Whether the database answers. A failure while it does not is an outage, not the
        message's fault."""
        try:
            with self.engine.connect() as connection:
                connection.execute(text("SELECT 1"))
        except Exception:
            return False
        return True

    async def inspect(
        self,
        tenant_id: UUID,
        request: InspectionRequested,
        publish: Callable[[Payload], Awaitable[None]],
    ) -> InspectionCompleted | InspectionFailed:
        """The inspection's result: its findings, or its failure (`budget_exceeded`,
        `no_documents`). Publishes InspectionStarted, then an InspectionProgressed after each item
        evaluated, through `publish`; a finished inspection publishes nothing and returns its
        stored result. Raises InspectionMismatch when the inspection's checkpoints are for
        another establishment; raises on anything else, for the consumer to retry: the next
        delivery resumes from the last checkpoint."""
        saver = TenantCheckpointSaver(self.engine, tenant_id)
        saved = await graph.load(saver, request.inspection_id)
        if saved.exists and not saved.is_for(request):
            raise InspectionMismatch("the checkpoints are for another establishment")
        ids = {"establishment_id": request.establishment_id, "inspection_id": request.inspection_id}
        if saved.finished:
            self._log("already finished", tenant_id, request)
            return self._result(request, saved)
        documents = await anyio.to_thread.run_sync(
            self._has_documents, tenant_id, request.establishment_id
        )
        if not documents:
            self._log("has no documents", tenant_id, request)
            return InspectionFailed(**ids, reason_code="no_documents", reason_text=NO_DOCUMENTS)

        async def started(themes: graph.Snapshot) -> None:
            total = sum(theme.items_total for theme in themes)
            await publish(InspectionStarted(**ids, items_total=total, themes=themes))

        async def progressed(themes: graph.Snapshot) -> None:
            await publish(
                InspectionProgressed(
                    **ids,
                    items_evaluated=sum(theme.items_evaluated for theme in themes),
                    items_total=sum(theme.items_total for theme in themes),
                    themes=themes,
                )
            )

        began = time.perf_counter()
        saved = await graph.run(
            saver,
            request,
            saved,
            model=self.model,
            settings=self.answers,
            max_output_tokens=self.inspection.inspection_max_tokens,
            concurrency=self.inspection.inspection_concurrency,
            excerpts=functools.partial(self._excerpts, tenant_id, request),
            # The requesting user from the message: usage is recorded per tenant and user.
            record_usage=functools.partial(
                record_usage,
                self.engine,
                tenant_id=tenant_id,
                user_id=request.requested_by,
                prices=self.answers.model_prices,
                feature="inspection",
            ),
            on_start=started,
            on_progress=progressed,
        )
        result = self._result(request, saved)
        self._log(
            "budget exceeded" if isinstance(result, InspectionFailed) else "completed",
            tenant_id,
            request,
            f": {len(saved.results)} items, {len(saved.values['findings'])} findings, "
            f"{time.perf_counter() - began:.1f} s",
        )
        return result

    def _has_documents(self, tenant_id: UUID, establishment_id: UUID) -> bool:
        with tenant_transaction(self.engine, tenant_id) as connection:
            params = {"establishment_id": establishment_id}
            return connection.execute(_HAS_DOCUMENTS, params).scalar_one()

    def _excerpts(
        self, tenant_id: UUID, request: InspectionRequested, item: ChecklistItem
    ) -> tuple[list[RetrievedChunk], list[RetrievedChunk]]:
        """The item's regulation text and the establishment's evidence for it, in one tenant
        transaction. With no regulation text the item is not judged, so nothing is searched."""
        with tenant_transaction(self.engine, tenant_id) as connection:
            regulation = regulation_chunks(
                connection, request.establishment_id, request.jurisdiction, item.section_paths
            )
            if not regulation:
                return [], []
            evidence = search_establishment(
                connection,
                request.establishment_id,
                evidence_query(item),
                embedder=self.embedder,
                reranker=self.reranker,
                settings=self.retrieval,
            )
        return regulation, evidence

    @staticmethod
    def _result(
        request: InspectionRequested, saved: graph.Saved
    ) -> InspectionCompleted | InspectionFailed:
        ids = {"establishment_id": request.establishment_id, "inspection_id": request.inspection_id}
        if saved.values.get("budget_exceeded"):
            return InspectionFailed(
                **ids, reason_code="budget_exceeded", reason_text=BUDGET_EXCEEDED
            )
        return InspectionCompleted(
            **ids,
            input_tokens=sum(result["input_tokens"] for result in saved.results),
            output_tokens=sum(result["output_tokens"] for result in saved.results),
            cost_usd=sum(result["cost_usd"] for result in saved.results),
            findings=tuple(Finding.model_validate(f) for f in saved.values["findings"]),
        )

    @staticmethod
    def _log(what: str, tenant_id: UUID, request: InspectionRequested, counts: str = "") -> None:
        # Ids and counts only: never titles, quotes or rationales.
        logger.info(
            "Inspection %s of establishment %s of tenant %s %s%s",
            request.inspection_id,
            request.establishment_id,
            tenant_id,
            what,
            counts,
            extra={"tenant_id": str(tenant_id)},
        )
