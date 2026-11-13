from dataclasses import dataclass
from uuid import UUID


@dataclass(frozen=True, slots=True)
class RetrievedChunk:
    """A corpus chunk returned by retrieval, with what an answer needs to cite it."""

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
