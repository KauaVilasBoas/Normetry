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
from normetry_ai.inspection.checklist import ChecklistItem, items_for
from normetry_ai.inspection.checkpoints import TenantCheckpointSaver
from normetry_ai.inspection.judge import JudgeModel, evidence_query, regulation_chunks
from normetry_ai.llm.embeddings import Embedder
from normetry_ai.llm.reranker import Reranker
from normetry_ai.llm.usage import UsageFeature, record_usage
from normetry_ai.messaging.contracts import (
    Finding,
    InspectionCompleted,
    InspectionFailed,
    InspectionFailureReason,
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
CORPUS_INCOMPLETE = (
    "The regulation corpus lacks sections this inspection's checklist cites. An operator must "
    "re-run corpus-ingest, then the inspection can be run again."
)


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
        stored result. Fails `error` (CORPUS_INCOMPLETE) before any model call when a section the
        checklist cites has no corpus chunk in the establishment's corpus version: a corpus
        ingested before a parser fix, which only `corpus-ingest` mends. Raises InspectionMismatch
        when the inspection's checkpoints are for another establishment; raises on anything else,
        for the consumer to retry: the next delivery resumes from the last checkpoint."""
        saver = TenantCheckpointSaver(self.engine, tenant_id)
        saved = await _saved_for(saver, request)
        if saved.finished:
            self._log("already finished", tenant_id, request)
            return self._result(request, saved)
        documents = await anyio.to_thread.run_sync(
            self._has_documents, tenant_id, request.establishment_id
        )
        if not documents:
            self._log("has no documents", tenant_id, request)
            return _no_documents(request)
        missing = await anyio.to_thread.run_sync(self._missing_sections, tenant_id, request)
        if missing:
            self._log_missing_sections(tenant_id, request, missing)
            return _corpus_incomplete(request)
        began = time.perf_counter()
        saved = await self._run_graph(saver, tenant_id, request, saved, publish)
        result = self._result(request, saved)
        self._log_run(tenant_id, request, saved, result, time.perf_counter() - began)
        return result

    async def _run_graph(
        self,
        saver: TenantCheckpointSaver,
        tenant_id: UUID,
        request: InspectionRequested,
        saved: graph.Saved,
        publish: Callable[[Payload], Awaitable[None]],
    ) -> graph.Saved:
        """Run the graph, or resume it from `saved`, publishing InspectionStarted first and an
        InspectionProgressed after each item evaluated."""
        return await graph.run(
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
                feature=UsageFeature.INSPECTION,
            ),
            on_start=lambda themes: publish(_started(request, themes)),
            on_progress=lambda themes: publish(_progressed(request, themes)),
        )

    def _has_documents(self, tenant_id: UUID, establishment_id: UUID) -> bool:
        with tenant_transaction(self.engine, tenant_id) as connection:
            params = {"establishment_id": establishment_id}
            return connection.execute(_HAS_DOCUMENTS, params).scalar_one()

    def _missing_sections(self, tenant_id: UUID, request: InspectionRequested) -> list[str]:
        """The section paths the request's checklist items cite that have no corpus chunk in the
        corpus version `regulation_chunks` reads for the establishment."""
        cited = {
            path
            for items in items_for(request.jurisdiction, request.establishment_type).values()
            for item in items
            for path in item.section_paths
        }
        with tenant_transaction(self.engine, tenant_id) as connection:
            chunks = regulation_chunks(
                connection, request.establishment_id, request.jurisdiction, sorted(cited)
            )
        return sorted(cited - {chunk.section_path for chunk in chunks})

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
        ids = _inspection_ids(request)
        if saved.values.get("budget_exceeded"):
            return InspectionFailed(
                **ids,
                reason_code=InspectionFailureReason.BUDGET_EXCEEDED,
                reason_text=BUDGET_EXCEEDED,
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

    @staticmethod
    def _log_missing_sections(
        tenant_id: UUID, request: InspectionRequested, missing: list[str]
    ) -> None:
        # Public corpus section paths: no tenant text.
        logger.warning(
            "Inspection %s of tenant %s failed: no corpus chunk for %d checklist sections (%s); "
            "re-run corpus-ingest",
            request.inspection_id,
            tenant_id,
            len(missing),
            ", ".join(missing),
            extra={"tenant_id": str(tenant_id)},
        )

    @classmethod
    def _log_run(
        cls,
        tenant_id: UUID,
        request: InspectionRequested,
        saved: graph.Saved,
        result: InspectionCompleted | InspectionFailed,
        seconds: float,
    ) -> None:
        cls._log(
            "budget exceeded" if isinstance(result, InspectionFailed) else "completed",
            tenant_id,
            request,
            f": {len(saved.results)} items, {len(saved.values['findings'])} findings, "
            f"{seconds:.1f} s",
        )


async def _saved_for(saver: TenantCheckpointSaver, request: InspectionRequested) -> graph.Saved:
    """The inspection's last checkpoint. Raises InspectionMismatch when its checkpoints were
    started for another establishment, jurisdiction or type."""
    saved = await graph.load(saver, request.inspection_id)
    if saved.exists and not saved.is_for(request):
        raise InspectionMismatch("the checkpoints are for another establishment")
    return saved


def _inspection_ids(request: InspectionRequested) -> dict[str, UUID]:
    """The ids every event of the inspection carries."""
    return {"establishment_id": request.establishment_id, "inspection_id": request.inspection_id}


def _no_documents(request: InspectionRequested) -> InspectionFailed:
    return InspectionFailed(
        **_inspection_ids(request),
        reason_code=InspectionFailureReason.NO_DOCUMENTS,
        reason_text=NO_DOCUMENTS,
    )


def _corpus_incomplete(request: InspectionRequested) -> InspectionFailed:
    return InspectionFailed(
        **_inspection_ids(request),
        reason_code=InspectionFailureReason.ERROR,
        reason_text=CORPUS_INCOMPLETE,
    )


def _started(request: InspectionRequested, themes: graph.Snapshot) -> InspectionStarted:
    total = sum(theme.items_total for theme in themes)
    return InspectionStarted(**_inspection_ids(request), items_total=total, themes=themes)


def _progressed(request: InspectionRequested, themes: graph.Snapshot) -> InspectionProgressed:
    return InspectionProgressed(
        **_inspection_ids(request),
        items_evaluated=sum(theme.items_evaluated for theme in themes),
        items_total=sum(theme.items_total for theme in themes),
        themes=themes,
    )
