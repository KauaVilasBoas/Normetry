"""Text normalization shared by the corpus parser and the golden dataset's quote check.

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
