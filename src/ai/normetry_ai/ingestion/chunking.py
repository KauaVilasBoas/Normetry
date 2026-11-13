"""Cut parsed sections into chunks: one per section, split only when a section is too long.

A chunk never crosses a section, so it keeps its `section_path`. A long section is cut at sentence
ends first, then at `;` or `,`, then at spaces, and the pieces are packed into chunks of similar
size. Joining a section's chunks with single spaces gives the section text back: every sentence
lives whole in exactly one chunk, so there is no overlap to deduplicate at retrieval time.
"""

import hashlib
import math
import re
from collections.abc import Iterable
from dataclasses import dataclass

from normetry_ai.ingestion.corpus import Section

# About one provision. Measured on the corpus: at most 535 BGE-M3 tokens per chunk (95% under 430),
# so a chunk and a question fit what the re-ranker (bge-reranker-v2-m3) scores at once. Sections
# run to 45,000 characters (FDA 1-201.10, the definitions); 852/2004 annex chapters to 7,200; RDC
# items stay under 1,200, so no RDC item is split.
CHUNK_LIMIT = 1500

# A sentence or clause end: ".", ";" or ":" (optionally followed by an FDA priority tag, "P", "Pf"
# or "C", which belongs to the provision before it), then a space before a capital, a digit, a
# quote or "(". Never before the tag itself.
# ponytail: abbreviations ("U.S. Department") also match; harmless unless a cut lands there.
_SENTENCE_END = re.compile(
    r"(?:(?<=[.;:])|(?<=[.;:] P)|(?<=[.;:] Pf)|(?<=[.;:] C))"
    r" (?!(?:Pf?|C)\b)(?=[(\"“A-ZÀ-Ý0-9])"
)
_SEPARATORS = (_SENTENCE_END, re.compile(r"(?<=[;,]) "), re.compile(" "))


@dataclass(frozen=True)
class Chunk:
    section_path: str
    content: str
    content_hash: str


def split_text(text: str, limit: int = CHUNK_LIMIT, level: int = 0) -> list[str]:
    """Cut normalized text (single spaces) into pieces of at most `limit` characters.

    Only a single word longer than the limit can come out longer.
    """
    if len(text) <= limit or level == len(_SEPARATORS):
        return [text]
    pieces = [
        part
        for piece in _SEPARATORS[level].split(text)
        for part in split_text(piece, limit, level + 1)
    ]
    # Close a chunk once it reaches the average size, so a section of 1.1 limits gives two chunks
    # of about half a limit each, not a full one and a scrap.
    target = len(text) / math.ceil(len(text) / limit)
    chunks = [pieces[0]]
    for piece in pieces[1:]:
        if len(chunks[-1]) >= target or len(chunks[-1]) + 1 + len(piece) > limit:
            chunks.append(piece)
        else:
            chunks[-1] += " " + piece
    return chunks


def content_hash(section_path: str, content: str) -> str:
    """SHA-256 of a chunk's location and text: the same text in another section is another chunk."""
    return hashlib.sha256(f"{section_path}\n{content}".encode()).hexdigest()


def chunk_sections(sections: Iterable[Section], limit: int = CHUNK_LIMIT) -> list[Chunk]:
    """The chunks of a source's sections, in document order, without duplicates."""
    chunks: dict[str, Chunk] = {}
    for section in sections:
        for content in split_text(section.text, limit):
            digest = content_hash(section.section_path, content)
            chunks.setdefault(digest, Chunk(section.section_path, content, digest))
    return list(chunks.values())
