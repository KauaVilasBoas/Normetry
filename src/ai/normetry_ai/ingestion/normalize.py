"""Text normalization shared by the corpus parser, the golden dataset's quote check and the
citation guard.

Parsed sections and quotes go through the same function, so a quote matches its section whatever
the line breaks and spacing of the source file. Standard library only: importable on its own.
"""

import re
import unicodedata

# PDFium marks a hyphen it found at a line end with U+FFFE and drops the break; U+00AD is the
# soft hyphen. Both mean "this word was split across lines".
_SPLIT_MARK = re.compile(r"[­￾]\s*")
# A letter, a hyphen at the end of the line, a lowercase letter on the next one: "prin-\nciples".
_LINE_BREAK_HYPHEN = re.compile(r"(?<=[^\W\d_])-[ \t]*\r?\n\s*(?=[a-zß-ÿ])")


def normalize_text(text: str) -> str:
    """Join line-break hyphenation, then collapse every whitespace run to one space (NFC).

    The text layer cannot tell hyphenation from a compound split at a line end, so a compound
    there loses its hyphen too (EU "corrosion-\\nresistant" becomes "corrosionresistant"). The FDA
    parser keeps its hyphens before calling this (that PDF is set without hyphenation).
    """
    text = unicodedata.normalize("NFC", text)
    text = _SPLIT_MARK.sub("", text)
    text = _LINE_BREAK_HYPHEN.sub("", text)
    return " ".join(text.split())


def locate(text: str, normalized_quote: str) -> tuple[int, int] | None:
    """Where the first match of a normalized quote in normalize_text(text) sits in `text` itself.

    Returns (start, end), end exclusive, with normalize_text(text[start:end]) == normalized_quote,
    or None when the quote is not there or the offsets cannot be mapped back exactly (a text that
    is not NFC, whose composed characters have no one-to-one position in the original).
    """
    if not normalized_quote or not unicodedata.is_normalized("NFC", text):
        return None
    # The steps of normalize_text, keeping the original position of every character left.
    kept = list(range(len(text)))
    for pattern in (_SPLIT_MARK, _LINE_BREAK_HYPHEN):
        current = "".join(text[i] for i in kept)
        removed = {k for match in pattern.finditer(current) for k in range(*match.span())}
        kept = [i for k, i in enumerate(kept) if k not in removed]
    chars: list[str] = []
    positions: list[int] = []
    space: int | None = None  # where a pending run of whitespace started
    for i in kept:
        if text[i].isspace():
            if chars and space is None:
                space = i
            continue
        if space is not None:
            chars.append(" ")
            positions.append(space)
            space = None
        chars.append(text[i])
        positions.append(i)
    normalized = "".join(chars)
    found = normalized.find(normalized_quote)
    if normalized != normalize_text(text) or found < 0:
        return None
    start, end = positions[found], positions[found + len(normalized_quote) - 1] + 1
    return (start, end) if normalize_text(text[start:end]) == normalized_quote else None
