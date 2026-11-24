import time
from collections.abc import Hashable, Iterable, Iterator, Sequence
from contextlib import contextmanager
from dataclasses import dataclass, replace
from enum import StrEnum
from uuid import UUID

from normetry_ai.llm.reranker import Reranker


class SourceType(StrEnum):
    """Where a chunk comes from: ai.chunks.source_type, which a CHECK holds to these values (the
    SQL spells them out too)."""

    REGULATION = "regulation"  # the public corpus
    TENANT_DOCUMENT = "tenant_document"  # one of a tenant's documents


@dataclass(frozen=True, slots=True)
class RetrievedChunk:
    """A chunk returned by retrieval, with what an answer needs to cite it.

    A regulation chunk (`hybrid.py`) fills the source fields from ai.sources. A tenant document
    chunk (`establishment.py`) fills the document fields; its source fields repeat them
    (`source_id` = document id, `source_title` = title or "", `source_version` = version number),
    and `jurisdiction` and `official_url` are "" (a tenant document has neither).
    """

    chunk_id: UUID
    content: str
    section_path: str | None
    language: str
    jurisdiction: str
    source_id: UUID
    source_title: str
    source_version: str
    official_url: str
    # Re-ranker score: higher is more relevant. The refusal threshold compares against it.
    score: float
    source_type: SourceType = SourceType.REGULATION
    document_id: UUID | None = None
    # From DocumentUploaded; None when the publisher sent none.
    document_title: str | None = None
    document_type: str | None = None
    document_version: int | None = None

    @property
    def is_document(self) -> bool:
        """A chunk of one of the tenant's documents, not of the regulation."""
        return self.source_type == SourceType.TENANT_DOCUMENT


def rerank(
    reranker: Reranker, query: str, candidates: Sequence[RetrievedChunk]
) -> list[RetrievedChunk]:
    """The candidates with the re-ranker's scores, from one call over all of them, best first
    (ties keep the given order). Shared by both searches."""
    scores = reranker.score(query, [chunk.content for chunk in candidates])
    return sorted(
        (replace(chunk, score=score) for chunk, score in zip(candidates, scores, strict=True)),
        key=lambda chunk: -chunk.score,
    )


@contextmanager
def timed(seconds: dict[str, float], step: str) -> Iterator[None]:
    """Store how long the block took, in seconds, as `seconds[step]` (the searches' step times,
    which they log and the evals report)."""
    started = time.perf_counter()
    yield
    seconds[step] = time.perf_counter() - started


def vector_literal(vector: Iterable[float]) -> str:
    """A vector in pgvector's text form ("[0.1,0.2]"), for `CAST(:embedding AS public.vector)`.
    Shared by both searches and both ingestions."""
    return "[" + ",".join(map(str, vector)) + "]"


def reciprocal_rank_fusion[T: Hashable](rankings: Iterable[Sequence[T]], k: int) -> list[T]:
    """Merge rankings: each item scores the sum of 1 / (k + rank) over the rankings it is in.

    Ties keep the order of first appearance, so the result is deterministic. Shared by the corpus
    search (`hybrid.py`) and the establishment search (`establishment.py`).
    """
    scores: dict[T, float] = {}
    for ranking in rankings:
        for rank, item in enumerate(ranking, start=1):
            scores[item] = scores.get(item, 0.0) + 1 / (k + rank)
    return sorted(scores, key=scores.__getitem__, reverse=True)
