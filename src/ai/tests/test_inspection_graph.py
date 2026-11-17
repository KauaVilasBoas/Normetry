"""The inspection graph (`inspection/graph.py`) with a fake judge (`fakes.EchoJudge`), which quotes
its excerpts so the real guard keeps its findings: findings, their order and actions, progress,
the token ceiling; on the shared PostgreSQL (marker `integration`), a run killed between two
themes that resumes from its checkpoints; and (markers `model`, `integration`) the run time of a
whole inspection on a synthetic establishment with the real embedder and re-ranker.

Offline but for the database: no test calls the Anthropic API.
"""

import asyncio
import gc
import hashlib
import json
import time
from collections import Counter
from collections.abc import Sequence
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import pytest
from conftest import AI_DIR
from fakes import EchoJudge, FakeContainer
from langgraph.checkpoint.base import BaseCheckpointSaver
from langgraph.checkpoint.memory import InMemorySaver
from sqlalchemy import Engine, text

from normetry_ai.ingestion.documents import BlobStore, DocumentWorker
from normetry_ai.inspection import graph
from normetry_ai.inspection.checklist import SEVERITIES, ChecklistItem, items_for
from normetry_ai.inspection.checkpoints import TenantCheckpointSaver
from normetry_ai.inspection.guard import UNVERIFIED_RATIONALE
from normetry_ai.inspection.worker import InspectionWorker
from normetry_ai.llm.claude import ModelUnavailable
from normetry_ai.llm.embeddings import Embedder, SentenceTransformerEmbedder
from normetry_ai.llm.reranker import CrossEncoderReranker
from normetry_ai.llm.usage import Usage
from normetry_ai.messaging.contracts import DocumentUploaded, InspectionRequested
from normetry_ai.retrieval.models import RetrievedChunk
from normetry_ai.settings import (
    AnswerSettings,
    EmbeddingSettings,
    InspectionSettings,
    RetrievalSettings,
)

SETTINGS = AnswerSettings(anthropic_api_key=None, answer_min_quote_words=4)
PLAN = items_for("US", "Restaurant")
ITEMS = [item for items in PLAN.values() for item in items]
TITLES = {item.title: item for item in ITEMS}
THEMES = list(PLAN)
# Items whose evidence search finds nothing, and items the judge finds non-compliant.
NO_EVIDENCE = {PLAN[THEMES[1]][0].id, PLAN[THEMES[-1]][-1].id}
NON_COMPLIANT = {
    items[0].title
    for items in PLAN.values()
    if len(items) > 1 and items[0].id not in NO_EVIDENCE  # without a document it is downgraded
}


def request(token_ceiling: int = 300_000) -> InspectionRequested:
    return InspectionRequested(
        establishment_id=uuid4(),
        inspection_id=uuid4(),
        jurisdiction="US",
        establishment_type="Restaurant",
        token_ceiling=token_ceiling,
        requested_by="user-1",
    )


def regulation(item: ChecklistItem) -> RetrievedChunk:
    return RetrievedChunk(
        chunk_id=uuid4(),
        content=f"Under {item.section_paths[0]} the food shall be kept safe at every step.",
        section_path=item.section_paths[0],
        language="en",
        jurisdiction="US",
        source_id=uuid4(),
        source_title="FDA Food Code",
        source_version="2026",
        official_url="https://example.org/food-code",
        score=1.0,
    )


def evidence(item: ChecklistItem) -> RetrievedChunk:
    document = uuid4()
    return RetrievedChunk(
        chunk_id=uuid4(),
        content=f"Our record for {item.id} shows the check was done and signed every day.",
        section_path="Page 1",
        language="en",
        jurisdiction="",
        source_id=document,
        source_title="Daily log",
        source_version="1",
        official_url="",
        score=0.9,
        source_type="tenant_document",
        document_id=document,
        document_title="Daily log",
        document_type="Log",
        document_version=1,
    )


def excerpts(item: ChecklistItem) -> tuple[list[RetrievedChunk], list[RetrievedChunk]]:
    return [regulation(item)], [] if item.id in NO_EVIDENCE else [evidence(item)]


class Run:
    """One inspection's runs, as deliveries of its message would make them."""

    def __init__(self, checkpointer: BaseCheckpointSaver, message: InspectionRequested) -> None:
        self.checkpointer, self.request = checkpointer, message
        self.recorded: list[Usage] = []
        self.started: list[graph.Snapshot] = []
        self.progress: list[graph.Snapshot] = []

    async def _started(self, snapshot: graph.Snapshot) -> None:
        self.started.append(snapshot)

    async def _progressed(self, snapshot: graph.Snapshot) -> None:
        self.progress.append(snapshot)

    def __call__(self, model: Any, concurrency: int = 4) -> graph.Saved:
        async def go() -> graph.Saved:
            saved = await graph.load(self.checkpointer, self.request.inspection_id)
            return await graph.run(
                self.checkpointer,
                self.request,
                saved,
                model=model,
                settings=SETTINGS,
                max_output_tokens=8192,
                concurrency=concurrency,
                excerpts=excerpts,
                record_usage=self.recorded.append,
                on_start=self._started,
                on_progress=self._progressed,
            )

        return asyncio.run(go())


def evaluated(snapshot: graph.Snapshot) -> int:
    return sum(theme.items_evaluated for theme in snapshot)


def test_an_inspection_judges_every_item_and_plans_an_action_for_each_finding() -> None:
    model = EchoJudge(verdicts=dict.fromkeys(NON_COMPLIANT, "non_compliant"))
    run = Run(InMemorySaver(), request())

    saved = run(model)

    findings = saved.values["findings"]
    assert sorted(model.judged) == sorted(TITLES)  # each item judged once
    assert sorted(f["itemId"] for f in findings) == sorted(item.id for item in ITEMS)
    # By severity, then in checklist order.
    order = [item.id for item in ITEMS]
    keys = [(SEVERITIES.index(f["severity"]), order.index(f["itemId"])) for f in findings]
    assert keys == sorted(keys)
    for finding in findings:
        item = TITLES[finding["title"]]
        assert (finding["itemId"], finding["theme"], finding["severity"]) == (
            item.id,
            item.theme,
            item.severity,
        )
        expected = (
            "insufficient_evidence"
            if item.id in NO_EVIDENCE
            else "non_compliant"
            if item.title in NON_COMPLIANT
            else "compliant"
        )
        assert finding["verdict"] == expected
        if expected == "compliant":
            assert finding["action"] is None and finding["evidenceCitations"]
        else:
            days = {"critical": 7, "major": 30, "minor": 90}[item.severity]
            assert finding["action"] == {"text": "Keep the record and show it.", "dueInDays": days}
    assert saved.values["budget_exceeded"] is False
    # Usage: one row per call, and the totals the result reports.
    assert len(run.recorded) == len(ITEMS)
    assert sum(r["input_tokens"] for r in saved.results) == sum(
        u.input_tokens for u in run.recorded
    )
    assert sum(r["cost_usd"] for r in saved.results) > 0


def test_progress_reports_every_item_with_a_full_snapshot_per_theme() -> None:
    run = Run(InMemorySaver(), request())

    run(EchoJudge())

    [started] = run.started
    assert [(t.theme, t.items_total, t.items_evaluated) for t in started] == [
        (theme, len(items), 0) for theme, items in PLAN.items()
    ]
    # One snapshot per item, each one more than the last, and the last one complete.
    assert [evaluated(snapshot) for snapshot in run.progress] == list(range(1, len(ITEMS) + 1))
    assert [(t.theme, t.items_evaluated) for t in run.progress[-1]] == [
        (theme, len(items)) for theme, items in PLAN.items()
    ]
    for earlier, later in zip(run.progress, run.progress[1:], strict=False):
        pairs = zip(earlier, later, strict=True)
        assert all(a.items_evaluated <= b.items_evaluated for a, b in pairs)


def test_a_finished_inspection_is_not_run_again() -> None:
    run = Run(InMemorySaver(), request())
    first = run(EchoJudge())
    model = EchoJudge()

    saved = asyncio.run(graph.load(run.checkpointer, run.request.inspection_id))

    assert saved.finished and saved.values["findings"] == first.values["findings"]
    assert model.judged == []


def test_a_finding_without_a_verified_quote_follows_the_guard() -> None:
    # The judge quotes a document passage that is not there: the guard downgrades the verdict.
    title = ITEMS[0].title

    class Misquoting(EchoJudge):
        async def judge(self, system: str, user: str) -> tuple[Any, Usage]:
            reply, usage = await super().judge(system, user)
            if title in user:
                reply["evidence_quotes"] = [{"id": "D1", "quote": "nothing like this was said"}]
            return reply, usage

    saved = Run(InMemorySaver(), request())(Misquoting())

    [finding] = [f for f in saved.values["findings"] if f["title"] == title]
    assert (finding["verdict"], finding["rationale"]) == (
        "insufficient_evidence",
        UNVERIFIED_RATIONALE,
    )


@pytest.mark.parametrize("ceiling", [1, 12_000], ids=["no-call", "a-few-calls"])
def test_a_low_ceiling_fails_budget_exceeded_with_no_findings(ceiling: int) -> None:
    model = EchoJudge()
    run = Run(InMemorySaver(), request(token_ceiling=ceiling))

    saved = run(model, concurrency=1)

    assert saved.values["budget_exceeded"] is True
    assert saved.values["findings"] == []
    # Never a call that could pass the ceiling: what was spent stays under it.
    assert sum(u.input_tokens + u.output_tokens for u in run.recorded) <= ceiling
    assert (len(model.judged) == 0) == (ceiling == 1)
    assert len(model.judged) < len(ITEMS)
    # A redelivery finds it finished: nothing more is spent.
    again = asyncio.run(graph.load(run.checkpointer, run.request.inspection_id))
    assert again.finished and again.values["budget_exceeded"] is True


def test_an_items_error_is_never_logged_with_its_text(caplog: pytest.LogCaptureFixture) -> None:
    # Every item past the first theme fails, with text that could quote stored rows: the run
    # stops on the first error and cancels the other branches before they read their futures.
    first_theme = {item.title for item in PLAN[THEMES[0]]}

    def fail(title: str) -> Exception | None:
        return None if title in first_theme else ModelUnavailable("SECRET stored row text")

    with pytest.raises(ModelUnavailable):
        Run(InMemorySaver(), request())(EchoJudge(fail=fail))
    gc.collect()  # asyncio logs an unread future's error when the future is collected

    assert "SECRET" not in caplog.text
    assert "never retrieved" not in caplog.text


# --- Resume from the checkpoints on PostgreSQL ------------------------------------------------


class Crash(Exception):
    """Stands in for the worker dying mid-run."""


@pytest.mark.integration
def test_a_run_killed_between_two_themes_resumes_without_judging_finished_items(
    app_ai: Engine,
) -> None:
    tenant, message = uuid4(), request()
    first_theme = {item.title for item in PLAN[THEMES[0]]}

    def crash_after_the_first_theme(title: str) -> Exception | None:
        # The first call once every item of the first theme is judged.
        return Crash() if first_theme <= set(killed.judged) else None

    killed = EchoJudge(fail=crash_after_the_first_theme)
    with pytest.raises(Crash):
        # One item at a time, so the first theme finishes before any other item starts.
        Run(TenantCheckpointSaver(app_ai, tenant), message)(killed, concurrency=1)
    assert set(killed.judged) == first_theme

    # The redelivery: another process, another saver, the same tenant and inspection.
    resumed, run = EchoJudge(), Run(TenantCheckpointSaver(app_ai, tenant), message)
    saved = run(resumed)

    assert not set(resumed.judged) & set(killed.judged)
    assert Counter(killed.judged + resumed.judged) == Counter(item.title for item in ITEMS)
    assert sorted(f["itemId"] for f in saved.values["findings"]) == sorted(i.id for i in ITEMS)
    # The resumed run starts from the stored progress, and the result counts every call once.
    [started] = run.started
    assert evaluated(started) == len(first_theme)
    assert len(saved.results) == len(ITEMS)
    # Another tenant's saver sees nothing of it, even with the inspection id.
    other = TenantCheckpointSaver(app_ai, uuid4())
    assert asyncio.run(graph.load(other, message.inspection_id)).exists is False


# --- Run time on a synthetic establishment (real embedder and re-ranker) --------------------

BAKERY = AI_DIR.parents[1] / "evals" / "establishments" / "bakery-austin" / "documents"
CONTENT_TYPES = {
    ".pdf": "application/pdf",
    ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    ".csv": "text/csv",
    ".txt": "text/plain",
}


def stub_regulation(corpus: Engine, items: Sequence[ChecklistItem]) -> None:
    """One corpus chunk per section the US checklist cites, in a test source: the inspection
    reads regulation text by section path, so the real corpus is not needed."""
    with corpus.begin() as connection:
        source = connection.execute(
            text(
                """
                INSERT INTO ai.sources (manifest_id, jurisdiction, code, title, version, language,
                                        official_url)
                VALUES (:manifest_id, 'US', 'food-code', 'FDA Food Code', '2026', 'en',
                        'https://example.org')
                RETURNING id
                """
            ),
            {"manifest_id": f"test-{uuid4()}"},
        ).scalar_one()
        paths = sorted({path for item in items for path in item.section_paths})
        connection.execute(
            text(
                """
                INSERT INTO ai.chunks (tenant_id, source_type, source_id, source_version,
                                       jurisdiction, language, section_path, content, content_hash)
                VALUES (NULL, 'regulation', :source, 1, 'US', 'en', :path, :content, :hash)
                """
            ),
            [
                {
                    "source": source,
                    "path": path,
                    "content": f"Section {path}: food shall be safe, unadulterated and honestly "
                    "presented, and the person in charge shall show how.",
                    "hash": uuid4().hex,
                }
                for path in paths
            ],
        )


def ingest_bakery(engine: Engine, embedder: Embedder, tenant: UUID, establishment: UUID) -> None:
    """Index Bakery Austin's nine synthetic documents as the consumer would."""
    blobs = FakeContainer()
    worker = DocumentWorker(engine, embedder, BlobStore(blobs))
    manifest = json.loads((BAKERY / "manifest.json").read_text(encoding="utf-8"))
    for entry in manifest["documents"]:
        data = (BAKERY / entry["file"]).read_bytes()
        document, version = uuid4(), uuid4()
        path = f"tenants/{tenant}/establishments/{establishment}/documents/{document}/{version}/f"
        blobs.blobs[path] = data
        message = DocumentUploaded(
            establishment_id=establishment,
            document_id=document,
            version_id=version,
            version_number=1,
            blob_path=path,
            content_type=CONTENT_TYPES[Path(entry["file"]).suffix],
            size=len(data),
            sha256=hashlib.sha256(data).hexdigest(),
            title=entry["title"],
            document_type=entry["type"],
        )
        assert worker.ingest(tenant, message, lambda: None) is not None


@pytest.mark.model
@pytest.mark.integration
def test_an_inspection_of_bakery_austin_runs_well_inside_the_acknowledgement_timeout(
    app_ai: Engine, corpus: Engine
) -> None:
    embedding, retrieval = EmbeddingSettings(_env_file=None), RetrievalSettings(_env_file=None)
    embedder = SentenceTransformerEmbedder(
        embedding.embedding_model, embedding.embedding_model_revision
    )
    reranker = CrossEncoderReranker(
        retrieval.reranker_model, retrieval.reranker_model_revision, retrieval.reranker_max_length
    )
    tenant, establishment = uuid4(), uuid4()
    stub_regulation(corpus, ITEMS)  # Bakery Austin is a US restaurant
    began = time.perf_counter()
    ingest_bakery(app_ai, embedder, tenant, establishment)
    ingested = time.perf_counter() - began
    message = request().model_copy(update={"establishment_id": establishment})
    worker = InspectionWorker(
        app_ai,
        embedder,
        reranker,
        EchoJudge(),
        answers=SETTINGS,
        retrieval=retrieval,
        inspection=InspectionSettings(_env_file=None),
    )
    published: list[Any] = []

    async def publish(payload: Any) -> None:
        published.append(payload)

    began = time.perf_counter()
    result = asyncio.run(worker.inspect(tenant, message, publish))
    seconds = time.perf_counter() - began

    print(f"\nBakery Austin: ingested in {ingested:.1f} s, inspected in {seconds:.1f} s")
    assert result.TYPE == "InspectionCompleted"
    assert len(result.findings) == len(ITEMS)
    assert sum(f.verdict == "compliant" for f in result.findings) > len(ITEMS) / 2
    assert published[-1].items_evaluated == len(ITEMS)
    assert seconds < 10 * 60  # RabbitMQ's acknowledgement timeout is 30 minutes
