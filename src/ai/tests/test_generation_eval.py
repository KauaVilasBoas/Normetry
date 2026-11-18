"""The generation eval: the real chat pipeline over the golden dataset, per chat model. PAID.

    uv run pytest -m generation    # from src/ai, NORMETRY_ANTHROPIC_API_KEY set (evals/README.md)

Skipped without NORMETRY_ANTHROPIC_API_KEY, before any fixture, so no container starts and
nothing is paid; CI never runs it. Ingests the corpus and the synthetic establishments like the
retrieval eval (same vector cache), then for each item of regulation-qa and document-qa: the
follow-up rewrite, retrieval as app_ai (a document item from its establishment, in its demo
tenant), and `answers.answer` with every model of NORMETRY_EVAL_MODELS at once. A follow-up runs
twice, rewritten by the rewrite model and by the free baseline. Writes a Markdown report and a
JSONL per model to NORMETRY_EVAL_REPORT_DIR (default src/ai/.data/generation-eval) and starts no
model call once the run has spent NORMETRY_EVAL_BUDGET_USD (default 5). Run it alone, like the
retrieval eval.
"""

import logging
import os
import threading
import time
from collections.abc import AsyncGenerator, Mapping
from contextlib import aclosing
from datetime import UTC, datetime
from decimal import Decimal
from functools import partial
from pathlib import Path
from typing import Any
from uuid import uuid4

import anyio
import pytest
from conftest import AI_DIR
from sqlalchemy import Engine, text
from test_retrieval_eval import EMBEDDING_CACHE, CachedEmbedder, index_demo

from normetry_ai.answers import Answered, Citation, Claim, ClaimModel, Refused, answer
from normetry_ai.conversations.rewrite import Turn, standalone_question
from normetry_ai.db import tenant_transaction
from normetry_ai.evals.dataset import DOCUMENT_DATASET, load_establishments, load_items
from normetry_ai.evals.generation import ModelAnswer, PassResult, jsonl, render
from normetry_ai.ingestion.corpus import load_manifest, parse_source
from normetry_ai.ingestion.ingest import ingest_source
from normetry_ai.llm.claude import ClaudeClaimModel, ClaudeRewriteModel
from normetry_ai.llm.embeddings import SentenceTransformerEmbedder
from normetry_ai.llm.reranker import CrossEncoderReranker
from normetry_ai.llm.usage import Usage
from normetry_ai.retrieval.hybrid import Retriever
from normetry_ai.retrieval.models import RetrievedChunk
from normetry_ai.retrieval.policy import jurisdictions_for
from normetry_ai.settings import (
    AnswerSettings,
    ConversationSettings,
    EmbeddingSettings,
    ModelPrice,
    RetrievalSettings,
)

pytestmark = [
    pytest.mark.generation,
    pytest.mark.model,
    pytest.mark.integration,
    # Evaluated before any fixture: without the key, no container, no model, no call.
    pytest.mark.skipif(
        not os.environ.get("NORMETRY_ANTHROPIC_API_KEY"),
        reason="paid eval: set NORMETRY_ANTHROPIC_API_KEY in the shell (evals/README.md)",
    ),
]

DEFAULT_MODELS = "claude-haiku-5-5,claude-sonnet-5-5,claude-opus-5-5"
# ClaudeClaimModel logs this when a call ends other than with end_turn (llm/claude.py).
STOPPED = "Claude %s stopped with %s"


class BudgetReached(Exception):
    pass


class Budget:
    """The summed cost of every model call of the run. Usage arrives from worker threads.

    Checked before each call is started. The models of one pass run at once, so the last pass can
    take the total past the limit by its own calls (about US$ 0.5 at most with the defaults).
    """

    def __init__(self, limit: Decimal, prices: Mapping[str, ModelPrice]) -> None:
        if not limit.is_finite() or limit < 0:
            raise ValueError("NORMETRY_EVAL_BUDGET_USD must be a finite, non-negative amount")
        self.limit, self.prices = limit, prices
        self.spent = Decimal(0)
        self._lock = threading.Lock()

    def record(self, usages: list[Usage], usage: Usage) -> None:
        # ponytail: a call that fails mid-stream reports input tokens only (answers/pipeline.py).
        cost = usage.cost_usd(self.prices[usage.model])
        with self._lock:
            self.spent += cost
            usages.append(usage)

    def check(self) -> None:
        with self._lock:
            if self.spent >= self.limit:
                raise BudgetReached

    def cost(self, usages: list[Usage]) -> Decimal:
        return sum((u.cost_usd(self.prices[u.model]) for u in usages), Decimal(0))


class StopReasons(logging.Handler):
    """The stop reason of each model's call in the current pass, from ClaudeClaimModel's warning."""

    def __init__(self) -> None:
        super().__init__()
        self.seen: dict[str, str] = {}

    def emit(self, record: logging.LogRecord) -> None:
        if record.msg == STOPPED and isinstance(record.args, tuple):
            model, reason = record.args
            self.seen[str(model)] = str(reason)


class CountingModel:
    """Counts the claim objects the model streams; the guard dropped those not shown."""

    def __init__(self, model: ClaimModel) -> None:
        self.model, self.raw = model, 0

    async def stream_claims(self, system: str, user: str) -> AsyncGenerator[Any | Usage, None]:
        items = self.model.stream_claims(system, user)
        async with aclosing(items):
            async for item in items:
                if not isinstance(item, Usage):
                    self.raw += 1
                yield item


def test_generation_eval(app_ai: Engine, corpus: Engine) -> None:
    models = [m.strip() for m in os.environ.get("NORMETRY_EVAL_MODELS", DEFAULT_MODELS).split(",")]
    models = [model for model in models if model]
    report_dir = Path(
        os.environ.get("NORMETRY_EVAL_REPORT_DIR", AI_DIR / ".data" / "generation-eval")
    )
    base = AnswerSettings()
    budget = Budget(Decimal(os.environ.get("NORMETRY_EVAL_BUDGET_USD", "5")), base.model_prices)
    # A model with no price fails here, before anything is paid.
    answer_settings = {model: AnswerSettings(chat_model=model) for model in models}
    claim_models = {model: ClaudeClaimModel(answer_settings[model]) for model in models}
    conversation = ConversationSettings(_env_file=None)
    rewrite_model = ClaudeRewriteModel(
        base, conversation.rewrite_model, conversation.rewrite_max_tokens
    )
    rewrite_settings = {
        mode: ConversationSettings(_env_file=None, followup_rewrite=mode)
        for mode in ("model", "baseline")
    }

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
    assert registered == {s["id"] for s, _ in parsed}, "run alone: uv run pytest -m generation"
    # Every establishment is indexed before any question, so a leak would have documents to leak.
    demo = index_demo(app_ai, embedder)
    establishments = load_establishments()
    # Document id -> (establishment name, file), to tell where a cited document comes from.
    owners = {
        document: (name, file)
        for name, establishment in establishments.items()
        for document, file in demo.files[establishment.folder].items()
    }

    # Load both models before timing anything.
    embedder.embed_query("warm up")
    reranker.score("warm up", ["warm up"])
    retriever = Retriever(embedder, reranker, retrieval)
    tenant = uuid4()  # regulation-qa items: a tenant with no documents, as in phase 1
    manifest_ids = {source["title"]: source["id"] for source, _ in parsed}
    items = load_items() + load_items(DOCUMENT_DATASET)
    stops = StopReasons()
    passes: list[PassResult] = []
    done: list[str] = []

    def claim_record(claim: Claim) -> dict:
        def citation(c: Citation) -> dict:
            record = {
                "chunk_id": str(c.chunk.chunk_id),
                "source": manifest_ids.get(c.chunk.source_title, c.chunk.source_title),
                "source_type": c.chunk.source_type,
                "section_path": c.chunk.section_path,
                "jurisdiction": c.chunk.jurisdiction,
                "language": c.chunk.language,
                "quote": c.quote,
            }
            if c.chunk.source_type == "tenant_document":
                name, file = owners.get(str(c.chunk.document_id), ("unknown", "unknown"))
                record |= {"establishment": name, "document": file}
            return record

        return {"text": claim.text, "citations": [citation(c) for c in claim.citations]}

    async def answer_with(
        model: str, question: str, chunks: list[RetrievedChunk], answers: dict[str, ModelAnswer]
    ) -> None:
        counting, usages = CountingModel(claim_models[model]), list[Usage]()
        result = ModelAnswer(outcome="unavailable")  # unless Answered or Refused comes
        started = time.perf_counter()
        events = answer(
            question,
            chunks,
            model=counting,
            settings=answer_settings[model],
            record_usage=partial(budget.record, usages),
        )
        async for event in events:
            if isinstance(event, Claim):
                if result.first_claim_seconds is None:
                    result.first_claim_seconds = time.perf_counter() - started
                result.claims.append(claim_record(event))
                result.cited |= {str(c.chunk.chunk_id): c.chunk.content for c in event.citations}
            elif isinstance(event, Refused):
                result.outcome, result.refusal_reason = "refused", event.reason.value
            elif isinstance(event, Answered):
                result.outcome = "answered"
        result.answer_seconds = time.perf_counter() - started
        result.raw_claims = counting.raw
        result.input_tokens = sum(
            u.input_tokens + u.cache_creation_input_tokens + u.cache_read_input_tokens
            for u in usages
        )
        result.output_tokens = sum(u.output_tokens for u in usages)
        result.cost_usd = budget.cost(usages)
        answers[model] = result

    async def run_pass(item: dict, mode: str) -> PassResult:
        *earlier, question = item["turns"]
        usages: list[Usage] = []
        started = time.perf_counter()
        if mode == "none":
            standalone = question
        else:
            if mode == "model":
                budget.check()
            standalone = await standalone_question(
                question,
                # The dataset holds the user's turns only: no answer comes between them.
                [Turn("user", turn) for turn in earlier],
                model=rewrite_model,
                settings=rewrite_settings[mode],
                record_usage=partial(budget.record, usages),
            )
        rewrite_seconds = time.perf_counter() - started

        # A document item is asked from its establishment, in its demo tenant (the chat's path).
        establishment = establishments.get(item.get("establishment", ""))
        asking = (
            (demo.tenant[establishment.folder], demo.establishment[establishment.folder])
            if establishment
            else (tenant, None)
        )

        def retrieve() -> list[RetrievedChunk]:
            with tenant_transaction(app_ai, asking[0]) as connection:
                scope = jurisdictions_for(item["jurisdiction"])
                return retriever.retrieve(connection, standalone, scope, establishment_id=asking[1])

        started = time.perf_counter()
        chunks = await anyio.to_thread.run_sync(retrieve)
        retrieval_seconds = time.perf_counter() - started

        budget.check()
        stops.seen.clear()
        answers: dict[str, ModelAnswer] = {}
        async with anyio.create_task_group() as group:
            for model in models:
                group.start_soon(answer_with, model, standalone, chunks, answers)
        for model, result in answers.items():
            result.stop_reason = stops.seen.get(model)
        return PassResult(
            item=item,
            rewrite=mode,
            standalone=standalone,
            retrieved=[chunk.content for chunk in chunks],
            rewrite_seconds=rewrite_seconds,
            retrieval_seconds=retrieval_seconds,
            answers=answers,
            rewrite_cost_usd=budget.cost(usages),
            targets=dict(establishment.targets)
            if establishment and item.get("attack") == "injection"
            else {},
        )

    async def run() -> None:
        for item in items:
            for mode in ("model", "baseline") if item["type"] == "follow_up" else ("none",):
                passes.append(await run_pass(item, mode))
            done.append(item["id"])

    claude_logger = logging.getLogger("normetry_ai.llm.claude")
    claude_logger.addHandler(stops)
    started_at = datetime.now(UTC)
    status = "complete"
    try:
        anyio.run(run)
    except BudgetReached:
        status = f"stopped at budget after item {done[-1] if done else 'none (no item ran)'}"
    except BaseException:
        status = f"interrupted by an error after item {done[-1] if done else 'none'}"
        raise
    finally:
        claude_logger.removeHandler(stops)
        # Also after an error: what was paid for is reported.
        knobs = ", ".join(f"{name.removeprefix('retrieval_')}={value}" for name, value in retrieval)
        report_dir.mkdir(parents=True, exist_ok=True)
        for model in models:
            settings = answer_settings[model]
            header = [
                f"- Run: {started_at:%Y-%m-%d %H:%M} UTC; {len(done)} of {len(items)} items,"
                f" {len(passes)} passes",
                f"- Chat model: `{model}`, effort {settings.chat_effort}, max_tokens"
                f" {settings.chat_max_tokens}; minimum quote words"
                f" {settings.answer_min_quote_words}, minimum re-ranker score"
                f" {settings.answer_min_rerank_score}",
                f"- Retrieval: `{embedder.name}`, re-ranker `{reranker.name}`; {knobs}",
                f"- Rewrite model: `{conversation.rewrite_model}` (effort low, max_tokens"
                f" {conversation.rewrite_max_tokens}), last"
                f" {conversation.chat_history_turns} turns",
                f"- Corpus: {chunk_count} chunks",
                f"- Budget: US$ {budget.spent:.4f} spent of US$ {budget.limit} (every model call of"
                f" the run: all models, both rewrite modes, rewrites); {status}",
            ]
            stem = f"{started_at:%Y-%m-%d}-generation-{model}"
            (report_dir / f"{stem}.md").write_text(render(passes, model, header), encoding="utf-8")
            (report_dir / f"{stem}.jsonl").write_text(jsonl(passes, model), encoding="utf-8")
