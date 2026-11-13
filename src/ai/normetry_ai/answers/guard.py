"""The citation guard (ADR 007): a claim reaches the caller only when every citation checks out.

A citation checks out when its excerpt id was retrieved for this answer and its quote, after the
same normalize_text the corpus parser and the golden dataset use, appears in that chunk's text.
One citation that fails drops the whole claim. The guard is deterministic and calls no model.
"""

import html
from collections.abc import Mapping
from dataclasses import dataclass

from normetry_ai.ingestion.normalize import locate, normalize_text
from normetry_ai.retrieval.models import RetrievedChunk


@dataclass(frozen=True, slots=True)
class Citation:
    """A checked citation, with the copy of the chunk an answer stores (text, source, URL)."""

    chunk: RetrievedChunk
    quote: str  # normalized: a substring of normalize_text(chunk.content)
    # chunk.content[quote_start:quote_end] is the quote as the chunk writes it; None when the
    # match cannot be mapped back to the original text exactly.
    quote_start: int | None = None
    quote_end: int | None = None


@dataclass(frozen=True, slots=True)
class Claim:
    text: str
    citations: tuple[Citation, ...]


def check_claim(
    raw: object, excerpts: Mapping[str, RetrievedChunk], min_quote_words: int
) -> Claim | None:
    """The claim with its citations resolved, or None when any part of it cannot be verified.

    `raw` is model output, so nothing about its shape is assumed.
    """
    if not isinstance(raw, dict):
        return None
    text, citations = raw.get("text"), raw.get("citations")
    if not isinstance(text, str) or not text.strip() or not isinstance(citations, list):
        return None
    checked = []
    for citation in citations:
        if not isinstance(citation, dict):
            return None
        excerpt_id, quote = citation.get("excerpt_id"), citation.get("quote")
        if not isinstance(excerpt_id, str) or not isinstance(quote, str):
            return None
        chunk = excerpts.get(excerpt_id)
        if chunk is None:
            return None
        # The prompt XML-escapes chunk text, so a quote copied from it may carry "&amp;".
        quote = normalize_text(html.unescape(quote))
        if len(quote.split()) < min_quote_words or quote not in normalize_text(chunk.content):
            return None
        start, end = locate(chunk.content, quote) or (None, None)
        checked.append(Citation(chunk, quote, start, end))
    if not checked:
        return None
    return Claim(text.strip(), tuple(checked))
