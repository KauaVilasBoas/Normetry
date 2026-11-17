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
