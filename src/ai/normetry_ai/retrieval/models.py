from collections.abc import Hashable, Iterable, Sequence
from dataclasses import dataclass
from typing import Literal
from uuid import UUID


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
    source_type: Literal["regulation", "tenant_document"] = "regulation"
    document_id: UUID | None = None
    # From DocumentUploaded; None when the publisher sent none.
    document_title: str | None = None
    document_type: str | None = None
    document_version: int | None = None


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
