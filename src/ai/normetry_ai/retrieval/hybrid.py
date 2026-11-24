"""Hybrid retrieval over the public corpus (ADR 006): vector top-k and full-text top-k, Reciprocal
Rank Fusion, then the re-ranker. Every step's output and time is kept in a `Trace`, logged, and
scored on its own by the retrieval evals.

Runs on a connection from `tenant_transaction`, as app_ai. Queries follow "Retrieval under RLS"
in src/ai/CLAUDE.md: `tenant_id IS NULL` so only the corpus indexes apply, the filter on the
(jurisdiction, language) pairs `policy.corpus_versions` gives, and the join to ai.sources on all
three foreign key columns (both from `sql.py`).

The chat also passes the conversation's establishment: its document candidates
(`establishment.document_candidates`) are re-ranked in the same call as the corpus candidates, so
the scores compare, and the top k keeps at most `retrieval_document_top_k` of them.
"""

import logging
from collections.abc import Sequence
from dataclasses import dataclass
from uuid import UUID

from sqlalchemy import Connection, text

from normetry_ai.db import current_tenant
from normetry_ai.llm.embeddings import Embedder
from normetry_ai.llm.reranker import Reranker
from normetry_ai.retrieval.establishment import document_candidates
from normetry_ai.retrieval.language import detect_language
from normetry_ai.retrieval.models import (
    RetrievedChunk,
    reciprocal_rank_fusion,
    rerank,
    timed,
    vector_literal,
)
from normetry_ai.retrieval.policy import JurisdictionScope, corpus_versions
from normetry_ai.retrieval.sql import (
    CORPUS_VERSIONS,
    TSQUERIES,
    any_word_terms,
    corpus_version_params,
    regulation_chunk,
    regulation_columns,
    sources_join,
    tsquery_for,
)
from normetry_ai.settings import RetrievalSettings

logger = logging.getLogger(__name__)

# Transaction-local knobs for the vector search. A filter that keeps one source of four would
# leave most HNSW candidates filtered away without iterative scan.
_PREPARE = text(
    """
    SELECT set_config('hnsw.ef_search', :ef_search, true),
           set_config('hnsw.iterative_scan', 'strict_order', true)
    """
)
# Each search runs on ai.chunks alone, so it can use the corpus indexes; the join to ai.sources
# then adds the source fields, on all three foreign key columns.
_WITH_SOURCES = f"""SELECT {regulation_columns("h")}
    FROM hits h
    {sources_join("h")}
    ORDER BY h.rank"""  # noqa: S608 (fixed fragments)
_VECTOR_SEARCH = text(
    f"""
    WITH hits AS (
        SELECT id, content, section_path, language, jurisdiction, source_id,
               embedding <=> CAST(:embedding AS public.vector) AS rank
        FROM ai.chunks
        WHERE tenant_id IS NULL
          AND (jurisdiction, language) IN (
              {CORPUS_VERSIONS})
        ORDER BY rank
        LIMIT :k
    )
    {_WITH_SOURCES}
    """  # noqa: S608 (fixed fragments)
)
# Any word of the question may match (OR); ts_rank orders by how often the words occur.
_TEXT_SEARCH = text(
    f"""
    WITH {TSQUERIES},
    hits AS (
        SELECT id, content, section_path, language, jurisdiction, source_id,
               -ts_rank(tsv, {tsquery_for("language")}) AS rank
        FROM ai.chunks, query
        WHERE tenant_id IS NULL
          AND (jurisdiction, language) IN (
              {CORPUS_VERSIONS})
          AND tsv @@ {tsquery_for("language")}
        ORDER BY rank
        LIMIT :k
    )
    {_WITH_SOURCES}
    """  # noqa: S608 (fixed fragments)
)


def best(chunks: list[RetrievedChunk], top_k: int, document_top_k: int) -> list[RetrievedChunk]:
    """The `top_k` best-scored chunks, at most `document_top_k` of them from tenant documents, so
    documents never crowd the regulation out. Ties keep the given order."""
    kept: list[RetrievedChunk] = []
    documents = 0
    for chunk in sorted(chunks, key=lambda chunk: -chunk.score):
        if chunk.is_document:
            if documents == document_top_k:
                continue
            documents += 1
        kept.append(chunk)
        if len(kept) == top_k:
            break
    return kept


@dataclass(frozen=True)
class Trace:
    """What each step returned (chunk ids, best first) and how long it took, in seconds."""

    language: str
    vector: list[UUID]
    text: list[UUID]
    fused: list[UUID]
    # The establishment's document candidates, fused, best first; empty without an establishment.
    documents: list[UUID]
    # The re-ranked result: what `retrieve` returns.
    chunks: list[RetrievedChunk]
    # The text of every candidate the searches returned, by chunk id.
    content: dict[UUID, str]
    seconds: dict[str, float]


@dataclass(frozen=True)
class _CorpusHits:
    """What the corpus searches returned: chunk ids, best first, the text of every hit by chunk
    id, and the fused hits the re-ranker scores (`retrieval_rerank_candidates` of them)."""

    vector: list[UUID]
    text: list[UUID]
    fused: list[UUID]
    content: dict[UUID, str]
    candidates: list[RetrievedChunk]


class Retriever:
    """Hybrid retrieval with one embedder and one re-ranker, loaded once and reused."""

    def __init__(self, embedder: Embedder, reranker: Reranker, settings: RetrievalSettings) -> None:
        self.embedder = embedder
        self.reranker = reranker
        self.settings = settings

    def retrieve(
        self,
        connection: Connection,
        question: str,
        scope: JurisdictionScope,
        *,
        establishment_id: UUID | None = None,
    ) -> list[RetrievedChunk]:
        """The corpus chunks most relevant to the question, best first, within the scope; with an
        establishment (from the core, never the client), its current documents' chunks too."""
        return self.trace(connection, question, scope, establishment_id=establishment_id).chunks

    def trace(
        self,
        connection: Connection,
        question: str,
        scope: JurisdictionScope,
        *,
        establishment_id: UUID | None = None,
    ) -> Trace:
        """`retrieve`, with the output of every step."""
        if not isinstance(scope, JurisdictionScope):
            raise TypeError("scope must come from policy.jurisdictions_for")
        settings = self.settings
        seconds: dict[str, float] = {}
        tenant = current_tenant(connection)
        language = detect_language(question)
        versions = corpus_versions(scope, language)

        with timed(seconds, "embed"):
            embedding = self.embedder.embed_query(question)
        hits = self._corpus_hits(connection, question, embedding, versions, seconds)
        documents = self._document_candidates(
            connection, establishment_id, question, embedding, seconds
        )
        # One call over both sources: their scores compare, for the top k and the refusal.
        with timed(seconds, "rerank"):
            ranked = rerank(self.reranker, question, hits.candidates + documents)
        chunks = best(ranked, settings.retrieval_top_k, settings.retrieval_document_top_k)
        trace = _trace(language, hits, documents, chunks, seconds)
        _log_retrieval(tenant, establishment_id, versions, trace, len(ranked))
        return trace

    def _corpus_hits(
        self,
        connection: Connection,
        question: str,
        embedding: Sequence[float],
        versions: Sequence[tuple[str, str]],
        seconds: dict[str, float],
    ) -> _CorpusHits:
        """The vector and full-text searches over the corpus versions, and their fusion."""
        settings = self.settings
        connection.execute(_PREPARE, {"ef_search": str(settings.retrieval_hnsw_ef_search)})
        params = corpus_version_params(versions)
        with timed(seconds, "vector"):
            vector_rows = connection.execute(
                _VECTOR_SEARCH,
                {
                    **params,
                    "embedding": vector_literal(embedding),
                    "k": settings.retrieval_vector_k,
                },
            ).all()
        with timed(seconds, "text"):
            text_rows = connection.execute(
                _TEXT_SEARCH,
                {**params, "terms": any_word_terms(question), "k": settings.retrieval_text_k},
            ).all()
        rows = {row.id: row for row in (*vector_rows, *text_rows)}
        vector = [row.id for row in vector_rows]
        lexical = [row.id for row in text_rows]
        fused = reciprocal_rank_fusion((vector, lexical), settings.retrieval_rrf_k)
        return _CorpusHits(
            vector=vector,
            text=lexical,
            fused=fused,
            content={chunk_id: row.content for chunk_id, row in rows.items()},
            # Not re-ranked yet: score 0.
            candidates=[
                regulation_chunk(rows[chunk_id], 0.0)
                for chunk_id in fused[: settings.retrieval_rerank_candidates]
            ],
        )

    def _document_candidates(
        self,
        connection: Connection,
        establishment_id: UUID | None,
        question: str,
        embedding: Sequence[float],
        seconds: dict[str, float],
    ) -> list[RetrievedChunk]:
        """The establishment's document candidates, the first `retrieval_document_k` of them;
        none without an establishment (corpus only)."""
        if establishment_id is None:
            return []
        settings = self.settings
        with timed(seconds, "documents"):
            return document_candidates(
                connection,
                establishment_id,
                question,
                embedding,
                vector_k=settings.retrieval_document_k,
                text_k=settings.retrieval_document_k,
                rrf_k=settings.retrieval_rrf_k,
            )[: settings.retrieval_document_k]


def _trace(
    language: str,
    hits: _CorpusHits,
    documents: list[RetrievedChunk],
    chunks: list[RetrievedChunk],
    seconds: dict[str, float],
) -> Trace:
    return Trace(
        language=language,
        vector=hits.vector,
        text=hits.text,
        fused=hits.fused,
        documents=[chunk.chunk_id for chunk in documents],
        chunks=chunks,
        content={**hits.content, **{chunk.chunk_id: chunk.content for chunk in documents}},
        seconds=seconds,
    )


def _log_retrieval(
    tenant: str,
    establishment_id: UUID | None,
    versions: Sequence[tuple[str, str]],
    trace: Trace,
    reranked: int,
) -> None:
    # Ids, counts and times only: never the question or chunk text.
    logger.info(
        "retrieval tenant=%s establishment=%s language=%s versions=%s vector=%d text=%d "
        "fused=%d documents=%d reranked=%d top_score=%.3f seconds=%s",
        tenant,
        establishment_id,
        trace.language,
        ",".join(f"{j}/{v}" for j, v in versions),
        len(trace.vector),
        len(trace.text),
        len(trace.fused),
        len(trace.documents),
        reranked,
        trace.chunks[0].score if trace.chunks else 0.0,
        {step: round(value, 3) for step, value in trace.seconds.items()},
        extra={"tenant_id": tenant},
    )
