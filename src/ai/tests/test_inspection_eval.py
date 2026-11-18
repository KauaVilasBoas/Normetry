"""The inspection eval: the real inspection of each synthetic establishment, scored against its
expected findings (evals/establishments/). PAID.

    uv run pytest -m inspection    # from src/ai, NORMETRY_ANTHROPIC_API_KEY set (evals/README.md)

Skipped without NORMETRY_ANTHROPIC_API_KEY, before any fixture, so no container starts and
nothing is paid; CI never runs it. Ingests the corpus like the retrieval eval (same vector cache),
then every establishment's documents with DocumentWorker over a fake blob store, in the demo's two
tenants (Demo Bistro: São Paulo and Lisboa; Demo Bakery: Austin), so evidence could leak between
two establishments of one tenant and between tenants. Then inspects each establishment with
InspectionWorker and an in-memory publisher (the graph, the evidence search, checkpoints and usage
on the shared PostgreSQL; no queue), the real re-ranker and the real ClaudeJudgeModel
(NORMETRY_INSPECTION_MODEL). Writes a Markdown report and a JSONL to NORMETRY_EVAL_REPORT_DIR
(default src/ai/.data/inspection-eval) and starts no judge call once the run has spent
NORMETRY_EVAL_BUDGET_USD (default 1). Run it alone, like the retrieval eval.
"""

import asyncio
import json
import logging
import os
import re
import time
from collections import Counter
from collections.abc import Mapping
from datetime import UTC, datetime
from decimal import Decimal
from pathlib import Path
from typing import Any
from uuid import uuid4
from xml.sax.saxutils import unescape

import pytest
from conftest import AI_DIR
from sqlalchemy import Engine, text
from test_generation_eval import STOPPED
from test_retrieval_eval import EMBEDDING_CACHE, TENANTS, CachedEmbedder, index_demo

from normetry_ai.evals.dataset import ESTABLISHMENTS
from normetry_ai.evals.inspection import InspectionRun, item_results, jsonl, metrics, render
from normetry_ai.ingestion.corpus import load_manifest, parse_source
from normetry_ai.ingestion.ingest import ingest_source
from normetry_ai.inspection import graph
from normetry_ai.inspection.checklist import items_for
from normetry_ai.inspection.checkpoints import TenantCheckpointSaver
from normetry_ai.inspection.judge import JudgeModel
from normetry_ai.inspection.worker import InspectionWorker
from normetry_ai.llm.claude import ClaudeJudgeModel
from normetry_ai.llm.embeddings import SentenceTransformerEmbedder
from normetry_ai.llm.reranker import CrossEncoderReranker
from normetry_ai.llm.usage import Usage
from normetry_ai.messaging.contracts import InspectionFailed, InspectionRequested, Payload
from normetry_ai.settings import (
    AnswerSettings,
    EmbeddingSettings,
    InspectionSettings,
    ModelPrice,
    RetrievalSettings,
)

pytestmark = [
    pytest.mark.inspection,
    pytest.mark.model,
    pytest.mark.integration,
    # Evaluated before any fixture: without the key, no container, no model, no call.
    pytest.mark.skipif(
        not os.environ.get("NORMETRY_ANTHROPIC_API_KEY"),
        reason="paid eval: set NORMETRY_ANTHROPIC_API_KEY in the shell (evals/README.md)",
    ),
]

ESTABLISHMENT_TYPE = "Restaurant"  # all three are restaurants
TOKEN_CEILING = 300_000  # the core's default per inspection (FR-020)
_TITLE = re.compile(r"<title>(.*?)</title>")


class EvalJudge:
    """The judge model under the run's budget: once the judge calls of the run have cost
    NORMETRY_EVAL_BUDGET_USD, no call starts (it raises the graph's BudgetExceeded, so the
    inspection stops as its token ceiling would stop it). The calls already in flight, up to
    NORMETRY_INSPECTION_CONCURRENCY, can take the total past the budget by their own cost.

    Keeps each reply by item id (from the prompt's <title>), for the guard's quote counts.
    """

    def __init__(self, model: JudgeModel, limit: Decimal, prices: Mapping[str, ModelPrice]) -> None:
        if not limit.is_finite() or limit < 0:
            raise ValueError("NORMETRY_EVAL_BUDGET_USD must be a finite, non-negative amount")
        self.model, self.limit, self.prices = model, limit, prices
        self.spent = Decimal(0)
        self.stopped = False
        self.titles: dict[str, str] = {}  # the current inspection's items: title -> id
        self.replies: dict[str, Any] = {}

    async def judge(self, system: str, user: str) -> tuple[Any, Usage]:
        if self.spent >= self.limit:
            self.stopped = True
            raise graph.BudgetExceeded
        reply, usage = await self.model.judge(system, user)
        self.spent += usage.cost_usd(self.prices[usage.model])
        title = _TITLE.search(user)
        if title and (item_id := self.titles.get(unescape(title.group(1)))):
            self.replies[item_id] = reply
        return reply, usage


class StopReasons(logging.Handler):
    """Judge calls that ended other than with end_turn, from ClaudeJudgeModel's warning."""

    def __init__(self) -> None:
        super().__init__()
        self.reasons: Counter[str] = Counter()

    def emit(self, record: logging.LogRecord) -> None:
        if record.msg == STOPPED and isinstance(record.args, tuple):
            self.reasons[str(record.args[1])] += 1


def test_inspection_eval(app_ai: Engine, corpus: Engine) -> None:
    report_dir = Path(
        os.environ.get("NORMETRY_EVAL_REPORT_DIR", AI_DIR / ".data" / "inspection-eval")
    )
    answers = AnswerSettings()
    inspection = InspectionSettings(_env_file=None)
    model = inspection.inspection_model
    # A judge model with no price fails here, before anything is paid.
    judge = EvalJudge(
        ClaudeJudgeModel(answers, model, inspection.inspection_max_tokens),
        Decimal(os.environ.get("NORMETRY_EVAL_BUDGET_USD", "1")),
        answers.model_prices,
    )

    try:
        parsed = [(source, parse_source(source)) for source in load_manifest()]
    except FileNotFoundError:
        pytest.skip(
            "Corpus not fetched: run `uv run corpus/scripts/fetch_corpus.py` from the root."
        )
    embedding = EmbeddingSettings(_env_file=None)
    retrieval = RetrievalSettings(_env_file=None)
    embedder = CachedEmbedder(
        SentenceTransformerEmbedder(embedding.embedding_model, embedding.embedding_model_revision),
        EMBEDDING_CACHE,
    )
    reranker = CrossEncoderReranker(
        retrieval.reranker_model, retrieval.reranker_model_revision, retrieval.reranker_max_length
    )
    for source, sections in parsed:
        ingest_source(corpus, source, sections, embedder)
    with app_ai.begin() as connection:
        registered = set(connection.execute(text("SELECT manifest_id FROM ai.sources")).scalars())
        chunk_count = connection.execute(text("SELECT count(*) FROM ai.chunks")).scalar_one()
    # Regulation text is read by section path: another test's sources would be judged by too.
    assert registered == {s["id"] for s, _ in parsed}, "run alone: uv run pytest -m inspection"
    embedder.embed_query("warm up")
    reranker.score("warm up", ["warm up"])

    # Every establishment is indexed before any is inspected, so a leak would have documents
    # to leak.
    demo = index_demo(app_ai, embedder)

    worker = InspectionWorker(
        app_ai,
        embedder,
        reranker,
        judge,
        answers=answers,
        retrieval=retrieval,
        inspection=inspection,
    )
    stops = StopReasons()

    def inspect(folder: str) -> InspectionRun:
        expected = json.loads(
            (ESTABLISHMENTS / folder / "expected-findings.json").read_text(encoding="utf-8")
        )
        tenant = demo.tenant[folder]
        run = InspectionRun(
            folder=folder,
            tenant=TENANTS[folder],
            expected=expected,
            documents=demo.files[folder],
            elsewhere={
                document: f"{other}/{file}"
                for other, files in demo.files.items()
                if other != folder
                for document, file in files.items()
            },
            ingest_seconds=demo.seconds[folder],
        )
        if judge.stopped:
            run.status = "not run: the eval budget was spent"
            return run
        message = InspectionRequested(
            establishment_id=demo.establishment[folder],
            inspection_id=uuid4(),
            jurisdiction=expected["jurisdiction"],
            establishment_type=ESTABLISHMENT_TYPE,
            token_ceiling=TOKEN_CEILING,
            requested_by="inspection-eval",
        )
        plan = items_for(expected["jurisdiction"], ESTABLISHMENT_TYPE)
        judge.titles = {item.title: item.id for items in plan.values() for item in items}
        stops.reasons.clear()
        published: list[Payload] = []

        async def publish(payload: Payload) -> None:
            published.append(payload)

        async def go() -> Any:
            began = time.perf_counter()
            try:
                return await worker.inspect(tenant, message, publish)
            finally:
                # Also after an error: what was judged is scored.
                run.seconds = time.perf_counter() - began
                saver = TenantCheckpointSaver(app_ai, tenant)
                saved = await graph.load(saver, message.inspection_id)
                run.items = item_results(saved.results, judge.replies)
                run.stop_reasons = dict(stops.reasons)

        try:
            result = asyncio.run(go())
        except BaseException as error:
            run.status = f"interrupted by {type(error).__name__}"
            raise
        finally:
            runs.append(run)
        if isinstance(result, InspectionFailed):
            run.status = (
                "stopped: the eval budget was spent"
                if judge.stopped
                else f"failed: {result.reason_code}"
            )
        return run

    claude_logger = logging.getLogger("normetry_ai.llm.claude")
    claude_logger.addHandler(stops)
    started_at = datetime.now(UTC)
    runs: list[InspectionRun] = []
    try:
        for folder in TENANTS:
            inspect(folder)
    finally:
        claude_logger.removeHandler(stops)
        knobs = ", ".join(f"{name.removeprefix('retrieval_')}={value}" for name, value in retrieval)
        header = [
            f"- Run: {started_at:%Y-%m-%d %H:%M} UTC; {len(runs)} of {len(TENANTS)} establishments"
            " inspected or attempted",
            f"- Judge: `{model}`, effort medium, max_tokens {inspection.inspection_max_tokens},"
            f" {inspection.inspection_concurrency} items at once; minimum quote words"
            f" {answers.answer_min_quote_words}; token ceiling {TOKEN_CEILING} per inspection",
            f"- Retrieval: `{embedder.name}`, re-ranker `{reranker.name}`; {knobs}",
            f"- Corpus and documents: {chunk_count} corpus chunks;"
            f" {sum(map(len, demo.files.values()))} documents in"
            f" {len(set(demo.tenant.values()))} tenants",
            f"- Budget: US$ {judge.spent:.4f} spent of US$ {judge.limit} (every judge call of the"
            " run)",
        ]
        report_dir.mkdir(parents=True, exist_ok=True)
        stem = f"{started_at:%Y-%m-%d}-inspection-{model}"
        (report_dir / f"{stem}.md").write_text(render(runs, model, header), encoding="utf-8")
        (report_dir / f"{stem}.jsonl").write_text(jsonl(runs), encoding="utf-8")

    m = metrics(runs)
    # Invariants, not quality targets: retrieval keeps to the establishment, the guard to the text.
    assert m["foreign evidence"] == 0, f"evidence from another establishment: see {stem}.md"
    assert m["unverified quotes"] == 0, f"a shown quote is not in its passage: see {stem}.md"
