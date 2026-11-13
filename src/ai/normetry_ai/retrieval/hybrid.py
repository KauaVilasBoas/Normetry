"""Hybrid retrieval over the public corpus (ADR 006): vector top-k and full-text top-k, Reciprocal
Rank Fusion, then the re-ranker. Every step's output and time is kept in a `Trace`, logged, and
scored on its own by the retrieval evals.

Runs on a connection from `tenant_transaction`, as app_ai. Queries follow "Retrieval under RLS"
in src/ai/CLAUDE.md: `tenant_id IS NULL` so only the corpus indexes apply, the filter on the
(jurisdiction, language) pairs `policy.corpus_versions` gives, and the join to ai.sources on all
three foreign key columns.
"""

import logging
import re
import time
from collections.abc import Hashable, Iterable, Sequence
from dataclasses import dataclass
from uuid import UUID

from sqlalchemy import Connection, text

from normetry_ai.llm.embeddings import Embedder
from normetry_ai.llm.reranker import Reranker
from normetry_ai.retrieval.language import detect_language
from normetry_ai.retrieval.models import RetrievedChunk
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


def reciprocal_rank_fusion[T: Hashable](rankings: Iterable[Sequence[T]], k: int) -> list[T]:
    """Merge rankings: each item scores the sum of 1 / (k + rank) over the rankings it is in.

    Ties keep the order of first appearance, so the result is deterministic.
    """
    scores: dict[T, float] = {}
    for ranking in rankings:
        for rank, item in enumerate(ranking, start=1):
            scores[item] = scores.get(item, 0.0) + 1 / (k + rank)
    return sorted(scores, key=scores.__getitem__, reverse=True)


@dataclass(frozen=True)
class Trace:
    """What each step returned (chunk ids, best first) and how long it took, in seconds."""

    language: str
    vector: list[UUID]
    text: list[UUID]
    fused: list[UUID]
    # The re-ranked result: what `retrieve` returns.
    chunks: list[RetrievedChunk]
    # The text of every candidate either search returned, by chunk id.
    content: dict[UUID, str]
    seconds: dict[str, float]


class Retriever:
    """Hybrid retrieval with one embedder and one re-ranker, loaded once and reused."""

    def __init__(self, embedder: Embedder, reranker: Reranker, settings: RetrievalSettings) -> None:
        self.embedder = embedder
        self.reranker = reranker
        self.settings = settings

    def retrieve(
        self, connection: Connection, question: str, scope: JurisdictionScope
    ) -> list[RetrievedChunk]:
        """The corpus chunks most relevant to the question, best first, within the scope."""
        return self.trace(connection, question, scope).chunks

    def trace(self, connection: Connection, question: str, scope: JurisdictionScope) -> Trace:
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

        candidates = fused[: settings.retrieval_rerank_candidates]
        started = time.perf_counter()
        scores = self.reranker.score(question, [rows[chunk_id].content for chunk_id in candidates])
        seconds["rerank"] = time.perf_counter() - started
        ranked = sorted(zip(candidates, scores, strict=True), key=lambda pair: -pair[1])
        chunks = [
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
            for chunk_id, score in ranked[: settings.retrieval_top_k]
        ]
        # Ids, counts and times only: never the question or chunk text.
        logger.info(
            "retrieval language=%s versions=%s vector=%d text=%d fused=%d reranked=%d "
            "top_score=%.3f seconds=%s",
            language,
            ",".join(f"{j}/{v}" for j, v in versions),
            len(vector),
            len(lexical),
            len(fused),
            len(candidates),
            chunks[0].score if chunks else 0.0,
            {step: round(value, 3) for step, value in seconds.items()},
        )
        return Trace(
            language=language,
            vector=vector,
            text=lexical,
            fused=fused,
            chunks=chunks,
            content={chunk_id: row.content for chunk_id, row in rows.items()},
            seconds=seconds,
        )
