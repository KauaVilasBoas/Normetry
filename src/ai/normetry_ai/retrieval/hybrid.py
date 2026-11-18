"""Hybrid retrieval over the public corpus (ADR 006): vector top-k and full-text top-k, Reciprocal
Rank Fusion, then the re-ranker. Every step's output and time is kept in a `Trace`, logged, and
scored on its own by the retrieval evals.

Runs on a connection from `tenant_transaction`, as app_ai. Queries follow "Retrieval under RLS"
in src/ai/CLAUDE.md: `tenant_id IS NULL` so only the corpus indexes apply, the filter on the
(jurisdiction, language) pairs `policy.corpus_versions` gives, and the join to ai.sources on all
three foreign key columns.

The chat also passes the conversation's establishment: its document candidates
(`establishment.document_candidates`) are re-ranked in the same call as the corpus candidates, so
the scores compare, and the top k keeps at most `retrieval_document_top_k` of them.
"""

import logging
import re
import time
from dataclasses import dataclass, replace
from uuid import UUID

from sqlalchemy import Connection, text

from normetry_ai.llm.embeddings import Embedder
from normetry_ai.llm.reranker import Reranker
from normetry_ai.retrieval.establishment import document_candidates
from normetry_ai.retrieval.language import detect_language
from normetry_ai.retrieval.models import RetrievedChunk, reciprocal_rank_fusion
from normetry_ai.retrieval.policy import JurisdictionScope, corpus_versions
from normetry_ai.settings import RetrievalSettings

logger = logging.getLogger(__name__)

# Transaction-local knobs for the vector search, and the check that a tenant is set. A filter that
# keeps one source of four would leave most HNSW candidates filtered away without iterative scan.
_PREPARE = text(
    """
    SELECT set_config('hnsw.ef_search', :ef_search, true),
           set_config('hnsw.iterative_scan', 'strict_order', true),
           current_setting('app.tenant_id', true)
    """
)
# Each search runs on ai.chunks alone, so it can use the corpus indexes; the join to ai.sources
# then adds the source fields, on all three foreign key columns.
_VECTOR_SEARCH = text(
    """
    WITH hits AS (
        SELECT id, content, section_path, language, jurisdiction, source_id,
               embedding <=> CAST(:embedding AS public.vector) AS rank
        FROM ai.chunks
        WHERE tenant_id IS NULL
          AND (jurisdiction, language) IN (
              SELECT * FROM unnest(CAST(:jurisdictions AS text[]), CAST(:languages AS text[])))
        ORDER BY rank
        LIMIT :k
    )
    SELECT h.id, h.content, h.section_path, h.language, h.jurisdiction, h.source_id,
           s.title AS source_title, s.version AS source_version, s.official_url
    FROM hits h
    JOIN ai.sources s
      ON s.id = h.source_id AND s.jurisdiction = h.jurisdiction AND s.language = h.language
    ORDER BY h.rank
    """
)
# Any word of the question may match (OR); ts_rank orders by how often the words occur. The
# question is parsed with each chunk's own configuration, as the generated tsv column is.
_TEXT_SEARCH = text(
    """
    WITH query AS (
        SELECT websearch_to_tsquery('portuguese', :terms) AS pt,
               websearch_to_tsquery('english', :terms) AS en
    ),
    hits AS (
        SELECT id, content, section_path, language, jurisdiction, source_id,
               -ts_rank(tsv, CASE language WHEN 'pt' THEN query.pt ELSE query.en END) AS rank
        FROM ai.chunks, query
        WHERE tenant_id IS NULL
          AND (jurisdiction, language) IN (
              SELECT * FROM unnest(CAST(:jurisdictions AS text[]), CAST(:languages AS text[])))
          AND tsv @@ CASE language WHEN 'pt' THEN query.pt ELSE query.en END
        ORDER BY rank
        LIMIT :k
    )
    SELECT h.id, h.content, h.section_path, h.language, h.jurisdiction, h.source_id,
           s.title AS source_title, s.version AS source_version, s.official_url
    FROM hits h
    JOIN ai.sources s
      ON s.id = h.source_id AND s.jurisdiction = h.jurisdiction AND s.language = h.language
    ORDER BY h.rank
    """
)


def best(chunks: list[RetrievedChunk], top_k: int, document_top_k: int) -> list[RetrievedChunk]:
    """The `top_k` best-scored chunks, at most `document_top_k` of them from tenant documents, so
    documents never crowd the regulation out. Ties keep the given order."""
    kept: list[RetrievedChunk] = []
    documents = 0
    for chunk in sorted(chunks, key=lambda chunk: -chunk.score):
        if chunk.source_type == "tenant_document":
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
        tenant = connection.execute(
            _PREPARE, {"ef_search": str(settings.retrieval_hnsw_ef_search)}
        ).one()[2]
        if not tenant:
            raise RuntimeError("retrieval runs inside tenant_transaction")
        language = detect_language(question)
        versions = corpus_versions(scope, language)
        params = {
            "jurisdictions": [jurisdiction for jurisdiction, _ in versions],
            "languages": [version for _, version in versions],
        }

        started = time.perf_counter()
        embedding = self.embedder.embed_query(question)
        seconds["embed"] = time.perf_counter() - started

        started = time.perf_counter()
        vector_rows = connection.execute(
            _VECTOR_SEARCH,
            {
                **params,
                "embedding": "[" + ",".join(map(str, embedding)) + "]",
                "k": settings.retrieval_vector_k,
            },
        ).all()
        seconds["vector"] = time.perf_counter() - started

        started = time.perf_counter()
        text_rows = connection.execute(
            _TEXT_SEARCH,
            {
                **params,
                # websearch_to_tsquery never fails on user input; "or" makes every word optional.
                "terms": " or ".join(re.findall(r"\w+", question)),
                "k": settings.retrieval_text_k,
            },
        ).all()
        seconds["text"] = time.perf_counter() - started

        rows = {row.id: row for row in (*vector_rows, *text_rows)}
        vector = [row.id for row in vector_rows]
        lexical = [row.id for row in text_rows]
        fused = reciprocal_rank_fusion((vector, lexical), settings.retrieval_rrf_k)

        documents: list[RetrievedChunk] = []
        if establishment_id is not None:
            started = time.perf_counter()
            documents = document_candidates(
                connection,
                establishment_id,
                question,
                embedding,
                vector_k=settings.retrieval_document_k,
                text_k=settings.retrieval_document_k,
                rrf_k=settings.retrieval_rrf_k,
            )[: settings.retrieval_document_k]
            seconds["documents"] = time.perf_counter() - started

        candidates = fused[: settings.retrieval_rerank_candidates]
        started = time.perf_counter()
        # One call over both sources: their scores compare, for the top k and the refusal.
        scores = self.reranker.score(
            question,
            [rows[chunk_id].content for chunk_id in candidates]
            + [chunk.content for chunk in documents],
        )
        seconds["rerank"] = time.perf_counter() - started
        regulation = [
            RetrievedChunk(
                chunk_id=chunk_id,
                content=rows[chunk_id].content,
                section_path=rows[chunk_id].section_path,
                language=rows[chunk_id].language,
                jurisdiction=rows[chunk_id].jurisdiction,
                source_id=rows[chunk_id].source_id,
                source_title=rows[chunk_id].source_title,
                source_version=rows[chunk_id].source_version,
                official_url=rows[chunk_id].official_url,
                score=score,
            )
            for chunk_id, score in zip(candidates, scores[: len(candidates)], strict=True)
        ]
        own = [
            replace(chunk, score=score)
            for chunk, score in zip(documents, scores[len(candidates) :], strict=True)
        ]
        chunks = best(regulation + own, settings.retrieval_top_k, settings.retrieval_document_top_k)
        # Ids, counts and times only: never the question or chunk text.
        logger.info(
            "retrieval tenant=%s establishment=%s language=%s versions=%s vector=%d text=%d "
            "fused=%d documents=%d reranked=%d top_score=%.3f seconds=%s",
            tenant,
            establishment_id,
            language,
            ",".join(f"{j}/{v}" for j, v in versions),
            len(vector),
            len(lexical),
            len(fused),
            len(documents),
            len(candidates) + len(documents),
            chunks[0].score if chunks else 0.0,
            {step: round(value, 3) for step, value in seconds.items()},
            extra={"tenant_id": tenant},
        )
        return Trace(
            language=language,
            vector=vector,
            text=lexical,
            fused=fused,
            documents=[chunk.chunk_id for chunk in documents],
            chunks=chunks,
            content={
                **{chunk_id: row.content for chunk_id, row in rows.items()},
                **{chunk.chunk_id: chunk.content for chunk in documents},
            },
            seconds=seconds,
        )
