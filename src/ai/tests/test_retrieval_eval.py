"""The retrieval eval: the real corpus, the real models, the golden dataset. See evals/README.md.

    uv run pytest -m eval    # from src/ai; Docker, the fetched corpus and about 5 GB of models

Ingests the corpus into the session's PostgreSQL with the configured embedding model, runs
retrieval on every item as app_ai, writes the report (NORMETRY_EVAL_REPORT, by default
src/ai/.data/retrieval-eval.md) and fails below the floors. Document vectors are cached in
src/ai/.data/embeddings, so only a changed chunk is embedded again; CI caches that directory.
Run it alone: other tests add sources that would pollute the results.
"""

import hashlib
import json
import os
import re
import time
from collections.abc import Sequence
from datetime import UTC, datetime
from pathlib import Path
from uuid import uuid4

import pytest
import torch
from conftest import AI_DIR
from sqlalchemy import Engine, text

from normetry_ai.db import tenant_transaction
from normetry_ai.evals.dataset import load_items
from normetry_ai.evals.retrieval import evaluate, metrics, out_of_scope, query_of, render
from normetry_ai.ingestion.chunking import chunk_sections
from normetry_ai.ingestion.corpus import load_manifest, parse_source
from normetry_ai.ingestion.ingest import ingest_source
from normetry_ai.ingestion.normalize import normalize_text
from normetry_ai.llm.embeddings import Embedder, SentenceTransformerEmbedder
from normetry_ai.llm.reranker import CrossEncoderReranker
from normetry_ai.retrieval.hybrid import Retriever, Trace
from normetry_ai.retrieval.policy import jurisdictions_for
from normetry_ai.settings import EmbeddingSettings, RetrievalSettings

pytestmark = [pytest.mark.eval, pytest.mark.model, pytest.mark.integration]

REPORT = Path(os.environ.get("NORMETRY_EVAL_REPORT", AI_DIR / ".data" / "retrieval-eval.md"))
EMBEDDING_CACHE = AI_DIR / ".data" / "embeddings"
# Regression floors for the re-ranked result over answerable items (first run: evals/reports/).
FLOORS = {"recall@10": 0.9, "MRR@10": 0.75}


class CachedEmbedder:
    """Document vectors kept in a JSON file per model, keyed by the text's SHA-256."""

    def __init__(self, embedder: Embedder, directory: Path) -> None:
        self.embedder = embedder
        self.name = embedder.name
        self.path = directory / (re.sub(r"[^\w.-]+", "_", embedder.name) + ".json")
        self.vectors = json.loads(self.path.read_text()) if self.path.exists() else {}

    def embed_documents(self, texts: Sequence[str]) -> list[list[float]]:
        keys = [hashlib.sha256(t.encode()).hexdigest() for t in texts]
        missing = {key: t for key, t in zip(keys, texts, strict=True) if key not in self.vectors}
        if missing:
            vectors = self.embedder.embed_documents(list(missing.values()))
            # ponytail: entries for chunks that no longer exist stay; delete the file to prune.
            self.vectors |= dict(zip(missing, vectors, strict=True))
            self.path.parent.mkdir(parents=True, exist_ok=True)
            self.path.write_text(json.dumps(self.vectors))
        return [self.vectors[key] for key in keys]

    def embed_query(self, text: str) -> list[float]:
        return self.embedder.embed_query(text)


def test_retrieval_eval(app_ai: Engine, corpus: Engine) -> None:
    try:
        parsed = [(source, parse_source(source)) for source in load_manifest()]
    except FileNotFoundError:
        if os.environ.get("CI"):
            raise
        pytest.skip(
            "Corpus not fetched: run `uv run corpus/scripts/fetch_corpus.py` from the root."
        )
    embedding = EmbeddingSettings(_env_file=None)
    settings = RetrievalSettings(_env_file=None)
    embedder = CachedEmbedder(
        SentenceTransformerEmbedder(embedding.embedding_model, embedding.embedding_model_revision),
        EMBEDDING_CACHE,
    )
    reranker = CrossEncoderReranker(
        settings.reranker_model, settings.reranker_model_revision, settings.reranker_max_length
    )

    started = time.perf_counter()
    for source, sections in parsed:
        ingest_source(corpus, source, sections, embedder)
    ingest_seconds = time.perf_counter() - started
    with app_ai.begin() as connection:
        registered = set(connection.execute(text("SELECT manifest_id FROM ai.sources")).scalars())
    assert registered == {source["id"] for source, _ in parsed}, "run alone: uv run pytest -m eval"

    # Load both models before timing anything.
    embedder.embed_query("warm up")
    reranker.score("warm up", ["warm up"])
    retriever = Retriever(embedder, reranker, settings)
    tenant = uuid4()

    def run(item: dict) -> Trace:
        with tenant_transaction(app_ai, tenant) as connection:
            scope = jurisdictions_for(item["jurisdiction"])
            return retriever.trace(connection, query_of(item), scope)

    items = load_items()
    started = time.perf_counter()
    results = evaluate(items, run)
    eval_seconds = time.perf_counter() - started

    # A quote that no single chunk holds cannot be a hit, whatever retrieval does.
    chunks = {
        source["id"]: [normalize_text(chunk.content) for chunk in chunk_sections(sections)]
        for source, sections in parsed
    }
    split = [
        f"{item['id']} {provision['section_path']}"
        for item in items
        for provision in item["provisions"]
        if not any(normalize_text(provision["quote"]) in c for c in chunks[provision["source_id"]])
    ]
    knobs = ", ".join(f"{name.removeprefix('retrieval_')}={value}" for name, value in settings)
    header = [
        f"- Run: {datetime.now(UTC):%Y-%m-%d %H:%M} UTC, CPU, {torch.get_num_threads()} threads",
        f"- Embedding model: `{embedder.name}`",
        f"- Re-ranker: `{reranker.name}`",
        f"- Settings: {knobs}",
        f"- Corpus: {sum(len(c) for c in chunks.values())} chunks; ingestion {ingest_seconds:.0f} s"
        f" (cached document vectors are not embedded again), eval {eval_seconds:.0f} s",
        "- Query: the item's turns joined into one (follow-up baseline), in the item's scope",
        f"- Quotes no single chunk holds: {', '.join(split) if split else 'none'}",
    ]
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(
        render(results, header, settings.retrieval_rerank_candidates), encoding="utf-8"
    )

    assert [result.item["id"] for result in results if out_of_scope(result)] == []
    scores = metrics(results, "reranked")
    assert {name: scores[name] >= floor for name, floor in FLOORS.items()} == dict.fromkeys(
        FLOORS, True
    ), f"{scores}; report: {REPORT}"
