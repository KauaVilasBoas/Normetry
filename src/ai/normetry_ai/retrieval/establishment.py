"""Search one establishment's documents (phase 2 design, "Establishment document search"): vector
top-k and full-text top-k over its tenant chunks, Reciprocal Rank Fusion, then the re-ranker.

Runs on a connection from `tenant_transaction`, as app_ai. RLS keeps the tenant, but tenant chunks
carry no establishment, so both searches join ai.tenant_documents on (tenant_id, source_id =
document_id) and keep the establishment's documents that are not deleted, at their indexed
version only. Tenant rows have no HNSW index: the vector search is exact KNN over the chunks the
join finds (src/ai/CLAUDE.md, "Retrieval under RLS").
"""

import logging
import re
import time
from uuid import UUID

from sqlalchemy import Connection, text

from normetry_ai.llm.embeddings import Embedder
from normetry_ai.llm.reranker import Reranker
from normetry_ai.retrieval.hybrid import reciprocal_rank_fusion
from normetry_ai.retrieval.models import RetrievedChunk
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
    tenant = connection.execute(text("SELECT current_setting('app.tenant_id', true)")).scalar()
    if not tenant:
        raise RuntimeError("retrieval runs inside tenant_transaction")
    seconds: dict[str, float] = {}

    started = time.perf_counter()
    embedding = embedder.embed_query(query)
    seconds["embed"] = time.perf_counter() - started

    params = {"establishment_id": establishment_id}
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
            "terms": " or ".join(re.findall(r"\w+", query)),
            "k": settings.retrieval_text_k,
        },
    ).all()
    seconds["text"] = time.perf_counter() - started

    rows = {row.id: row for row in (*vector_rows, *text_rows)}
    fused = reciprocal_rank_fusion(
        ([row.id for row in vector_rows], [row.id for row in text_rows]), settings.retrieval_rrf_k
    )
    candidates = fused[: settings.retrieval_establishment_rerank_candidates]
    started = time.perf_counter()
    scores = reranker.score(query, [rows[chunk_id].content for chunk_id in candidates])
    seconds["rerank"] = time.perf_counter() - started
    ranked = sorted(zip(candidates, scores, strict=True), key=lambda pair: -pair[1])
    chunks = []
    for chunk_id, score in ranked[: settings.retrieval_establishment_top_k]:
        row = rows[chunk_id]
        chunks.append(
            RetrievedChunk(
                chunk_id=chunk_id,
                content=row.content,
                section_path=row.section_path,
                language=row.language,
                jurisdiction="",
                source_id=row.document_id,
                source_title=row.title or "",
                source_version=str(row.version),
                official_url="",
                score=score,
                source_type="tenant_document",
                document_id=row.document_id,
                document_title=row.title,
                document_type=row.document_type,
                document_version=row.version,
            )
        )
    # Ids, counts and times only: never the query, titles or chunk text.
    logger.info(
        "establishment search tenant=%s establishment=%s vector=%d text=%d fused=%d "
        "reranked=%d documents=%d top_score=%.3f seconds=%s",
        tenant,
        establishment_id,
        len(vector_rows),
        len(text_rows),
        len(fused),
        len(candidates),
        len({chunk.document_id for chunk in chunks}),
        chunks[0].score if chunks else 0.0,
        {step: round(value, 3) for step, value in seconds.items()},
        extra={"tenant_id": tenant},
    )
    return chunks
