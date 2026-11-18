"""The retrieval eval: the real corpus, the real models, the golden dataset. See evals/README.md.

    uv run pytest -m eval    # from src/ai; Docker, the fetched corpus and about 5 GB of models

Ingests the corpus into the session's PostgreSQL with the configured embedding model, then the
three synthetic establishments' documents with DocumentWorker (fake blob store) in the demo's two
tenants. Runs the chat's retrieval as app_ai: every corpus item from an establishment with no
documents (scored against the floors) and again from the synthetic establishment of its
jurisdiction, and every document-qa item from its establishment. Writes the report
(NORMETRY_EVAL_REPORT, by default src/ai/.data/retrieval-eval.md) and fails below the floors or on
a document chunk of another establishment or tenant. Document vectors are cached in
src/ai/.data/embeddings, so only a changed chunk is embedded again; CI caches that directory.
Run it alone: other tests add sources that would pollute the results.
"""

import hashlib
import json
import os
import re
import time
from collections.abc import Sequence
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from uuid import UUID, uuid4

import pytest
import torch
from conftest import AI_DIR
from fakes import FakeContainer
from sqlalchemy import Engine, text

from normetry_ai.db import tenant_transaction
from normetry_ai.evals.dataset import (
    CONTENT_TYPES,
    DOCUMENT_DATASET,
    ESTABLISHMENTS,
    load_establishments,
    load_items,
)
from normetry_ai.evals.retrieval import (
    evaluate,
    evaluate_documents,
    foreign_documents,
    metrics,
    out_of_scope,
    query_of,
    render,
    render_documents,
)
from normetry_ai.ingestion.chunking import chunk_sections
from normetry_ai.ingestion.corpus import load_manifest, parse_source
from normetry_ai.ingestion.documents import BlobStore, DocumentWorker
from normetry_ai.ingestion.ingest import ingest_source
from normetry_ai.ingestion.normalize import normalize_text
from normetry_ai.llm.embeddings import Embedder, SentenceTransformerEmbedder
from normetry_ai.llm.reranker import CrossEncoderReranker
from normetry_ai.messaging.contracts import DocumentUploaded
from normetry_ai.retrieval.hybrid import Retriever, Trace
from normetry_ai.retrieval.policy import jurisdictions_for
from normetry_ai.settings import EmbeddingSettings, RetrievalSettings

pytestmark = [pytest.mark.eval, pytest.mark.model, pytest.mark.integration]

REPORT = Path(os.environ.get("NORMETRY_EVAL_REPORT", AI_DIR / ".data" / "retrieval-eval.md"))
EMBEDDING_CACHE = AI_DIR / ".data" / "embeddings"
# Regression floors for the re-ranked result over answerable corpus items, asked from an
# establishment with no documents (first run: evals/reports/).
FLOORS = {"recall@10": 0.9, "MRR@10": 0.75}
# Folder -> the demo tenant that owns it (DemoTenantSeed in src/core).
TENANTS = {
    "bakery-austin": "Demo Bakery",
    "bistro-lisboa": "Demo Bistro",
    "bistro-sao-paulo": "Demo Bistro",
}
# The synthetic establishment a corpus item of each jurisdiction is asked from, with documents.
HOME = {"US": "bakery-austin", "EU": "bistro-lisboa", "BR": "bistro-sao-paulo"}


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


@dataclass
class Demo:
    """The three synthetic establishments, indexed in the demo's two tenants."""

    tenant: dict[str, UUID]  # folder -> its tenant's id
    establishment: dict[str, UUID]  # folder -> establishment id
    files: dict[str, dict[str, str]]  # folder -> document id -> file
    seconds: dict[str, float]  # folder -> indexing time

    def own(self, folder: str) -> set[UUID]:
        return {UUID(document) for document in self.files[folder]}


def ingest(
    worker: DocumentWorker, blobs: FakeContainer, tenant: UUID, establishment: UUID, folder: Path
) -> dict[str, str]:
    """Index one establishment's documents as the consumer would: document id -> file."""
    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    files = {}
    for entry in manifest["documents"]:
        data = (folder / entry["file"]).read_bytes()
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
        assert worker.ingest(tenant, message, lambda: None) is not None, entry["file"]
        files[str(document)] = entry["file"]
    return files


def index_demo(engine: Engine, embedder: Embedder) -> Demo:
    """Index every synthetic establishment with DocumentWorker over a fake blob store (the real
    parser and the given embedder), all before any search, so a leak would have documents to
    leak: Demo Bistro holds São Paulo and Lisboa, Demo Bakery holds Austin."""
    tenants = {name: uuid4() for name in TENANTS.values()}
    demo = Demo({}, {}, {}, {})
    blobs = FakeContainer()
    worker = DocumentWorker(engine, embedder, BlobStore(blobs))
    for folder, tenant in TENANTS.items():
        demo.tenant[folder], demo.establishment[folder] = tenants[tenant], uuid4()
        began = time.perf_counter()
        demo.files[folder] = ingest(
            worker,
            blobs,
            demo.tenant[folder],
            demo.establishment[folder],
            ESTABLISHMENTS / folder / "documents",
        )
        demo.seconds[folder] = time.perf_counter() - began
    return demo


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
    demo = index_demo(app_ai, embedder)

    # Load both models before timing anything.
    embedder.embed_query("warm up")
    reranker.score("warm up", ["warm up"])
    retriever = Retriever(embedder, reranker, settings)

    def asked_from(tenant: UUID, establishment: UUID, item: dict) -> Trace:
        """The chat's path: the conversation's tenant and establishment."""
        with tenant_transaction(app_ai, tenant) as connection:
            scope = jurisdictions_for(item["jurisdiction"])
            return retriever.trace(
                connection, query_of(item), scope, establishment_id=establishment
            )

    folders = {name: e.folder for name, e in load_establishments().items()}

    def folder_of(item: dict) -> str:
        """A document item's own establishment; for a corpus item, its jurisdiction's."""
        if "establishment" in item:
            return folders[item["establishment"]]
        return HOME[item["jurisdiction"]]

    def from_demo(item: dict) -> Trace:
        folder = folder_of(item)
        return asked_from(demo.tenant[folder], demo.establishment[folder], item)

    # Corpus items from an establishment with no documents, in a tenant with none.
    empty = (uuid4(), uuid4())
    items = load_items()
    started = time.perf_counter()
    results = evaluate(items, lambda item: asked_from(*empty, item))
    eval_seconds = time.perf_counter() - started
    # Again, with documents; then the document questions.
    crowded = evaluate(items, from_demo)
    document_items = load_items(DOCUMENT_DATASET)
    started = time.perf_counter()
    documents = evaluate_documents(document_items, from_demo)
    document_seconds = time.perf_counter() - started
    foreign = [
        f"{result.item['id']}: {chunk}"
        for result in (*crowded, *documents)
        for chunk in foreign_documents(result.trace, demo.own(folder_of(result.item)))
    ]

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
        f"- Documents: {sum(map(len, demo.files.values()))} in {len(set(demo.tenant.values()))}"
        f" tenants, indexed in {sum(demo.seconds.values()):.0f} s; {len(document_items)} document"
        f" questions in {document_seconds:.0f} s",
        "- Query: the item's turns joined into one (follow-up baseline), in the item's scope",
        f"- Quotes no single chunk holds: {', '.join(split) if split else 'none'}",
    ]
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(
        render(results, header, settings.retrieval_rerank_candidates)
        + "\n"
        + render_documents(documents, results, crowded, foreign, settings.retrieval_document_top_k),
        encoding="utf-8",
    )

    leaving = [r.item["id"] for r in (*results, *crowded, *documents) if out_of_scope(r)]
    assert leaving == []
    assert foreign == [], f"document chunks of another establishment or tenant; report: {REPORT}"
    scores = metrics(results, "reranked")
    assert {name: scores[name] >= floor for name, floor in FLOORS.items()} == dict.fromkeys(
        FLOORS, True
    ), f"{scores}; report: {REPORT}"
