"""Search one establishment's documents (phase 2 design, "Establishment document search"): vector
top-k and full-text top-k over its tenant chunks, Reciprocal Rank Fusion, then the re-ranker.

Runs on a connection from `tenant_transaction`, as app_ai. RLS keeps the tenant, but tenant chunks
carry no establishment, so both searches join ai.tenant_documents on (tenant_id, source_id =
document_id) and keep the establishment's documents that are not deleted, at their indexed
version only. Tenant rows have no HNSW index: the vector search is exact KNN over the chunks the
join finds (src/ai/CLAUDE.md, "Retrieval under RLS").

`document_candidates` is the search without the re-ranker: the chat (`hybrid.Retriever` with an
establishment) re-ranks its candidates together with the corpus's, in one call.
"""

import logging
import re
from collections.abc import Sequence
from dataclasses import replace
from uuid import UUID

from sqlalchemy import Connection, text

from normetry_ai.db import current_tenant
from normetry_ai.llm.embeddings import Embedder
from normetry_ai.llm.reranker import Reranker
from normetry_ai.retrieval.models import (
    RetrievedChunk,
    reciprocal_rank_fusion,
    timed,
    vector_literal,
)
from normetry_ai.settings import RetrievalSettings

logger = logging.getLogger(__name__)

# The establishment's current document versions and their chunks. source_type is implied by the
# join (only tenant rows have a tenant), and stated so a corpus row can never match.
_DOCUMENT_CHUNKS = """
    FROM ai.tenant_documents d
    JOIN ai.chunks c
      ON c.tenant_id = d.tenant_id AND c.source_type = 'tenant_document'
     AND c.source_id = d.document_id AND c.source_version = d.indexed_version
"""
_COLUMNS = """
    c.id, c.content, c.section_path, c.language, d.document_id, d.indexed_version AS version,
    d.title, d.document_type
"""
_VECTOR_SEARCH = text(
    f"""
    SELECT {_COLUMNS}, c.embedding <=> CAST(:embedding AS public.vector) AS rank
    {_DOCUMENT_CHUNKS}
    WHERE d.establishment_id = :establishment_id AND d.deleted_at IS NULL
    ORDER BY rank
    LIMIT :k
    """
)
# As in hybrid.py: any word may match (OR), and the query is parsed with each chunk's own
# configuration, as its generated tsv column is.
_TEXT_SEARCH = text(
    f"""
    WITH query AS (
        SELECT websearch_to_tsquery('portuguese', :terms) AS pt,
               websearch_to_tsquery('english', :terms) AS en
    )
    SELECT {_COLUMNS},
           -ts_rank(c.tsv, CASE c.language WHEN 'pt' THEN query.pt ELSE query.en END) AS rank
    {_DOCUMENT_CHUNKS}
    CROSS JOIN query
    WHERE d.establishment_id = :establishment_id AND d.deleted_at IS NULL
      AND c.tsv @@ CASE c.language WHEN 'pt' THEN query.pt ELSE query.en END
    ORDER BY rank
    LIMIT :k
    """
)


def document_candidates(
    connection: Connection,
    establishment_id: UUID,
    query: str,
    embedding: Sequence[float],
    *,
    vector_k: int,
    text_k: int,
    rrf_k: int,
) -> list[RetrievedChunk]:
    """The establishment's current document chunks for the query, fused with Reciprocal Rank
    Fusion, best first, not re-ranked yet (`score` 0). `embedding` is the query's."""
    current_tenant(connection)
    params = {"establishment_id": establishment_id}
    vector_rows = connection.execute(
        _VECTOR_SEARCH,
        {**params, "embedding": vector_literal(embedding), "k": vector_k},
    ).all()
    text_rows = connection.execute(
        _TEXT_SEARCH,
        # websearch_to_tsquery never fails on user input; "or" makes every word optional.
        {**params, "terms": " or ".join(re.findall(r"\w+", query)), "k": text_k},
    ).all()
    rows = {row.id: row for row in (*vector_rows, *text_rows)}
    fused = reciprocal_rank_fusion(
        ([row.id for row in vector_rows], [row.id for row in text_rows]), rrf_k
    )
    return [
        RetrievedChunk(
            chunk_id=chunk_id,
            content=rows[chunk_id].content,
            section_path=rows[chunk_id].section_path,
            language=rows[chunk_id].language,
            jurisdiction="",
            source_id=rows[chunk_id].document_id,
            source_title=rows[chunk_id].title or "",
            source_version=str(rows[chunk_id].version),
            official_url="",
            score=0.0,
            source_type="tenant_document",
            document_id=rows[chunk_id].document_id,
            document_title=rows[chunk_id].title,
            document_type=rows[chunk_id].document_type,
            document_version=rows[chunk_id].version,
        )
        for chunk_id in fused
    ]


def search_establishment(
    connection: Connection,
    establishment_id: UUID,
    query: str,
    *,
    embedder: Embedder,
    reranker: Reranker,
    settings: RetrievalSettings,
) -> list[RetrievedChunk]:
    """The establishment's document chunks most relevant to the query, best first.

    The establishment comes from the core (the queue message or `X-Normetry-Establishment-Id`),
    never from the client; the tenant is the transaction's.
    """
    tenant = current_tenant(connection)
    seconds: dict[str, float] = {}

    with timed(seconds, "embed"):
        embedding = embedder.embed_query(query)
    with timed(seconds, "search"):
        fused = document_candidates(
            connection,
            establishment_id,
            query,
            embedding,
            vector_k=settings.retrieval_vector_k,
            text_k=settings.retrieval_text_k,
            rrf_k=settings.retrieval_rrf_k,
        )
    candidates = fused[: settings.retrieval_establishment_rerank_candidates]
    with timed(seconds, "rerank"):
        scores = reranker.score(query, [chunk.content for chunk in candidates])
    ranked = sorted(
        (replace(chunk, score=score) for chunk, score in zip(candidates, scores, strict=True)),
        key=lambda chunk: -chunk.score,
    )
    chunks = ranked[: settings.retrieval_establishment_top_k]
    # Ids, counts and times only: never the query, titles or chunk text.
    logger.info(
        "establishment search tenant=%s establishment=%s fused=%d reranked=%d documents=%d "
        "top_score=%.3f seconds=%s",
        tenant,
        establishment_id,
        len(fused),
        len(candidates),
        len({chunk.document_id for chunk in chunks}),
        chunks[0].score if chunks else 0.0,
        {step: round(value, 3) for step, value in seconds.items()},
        extra={"tenant_id": tenant},
    )
    return chunks
