"""XML helpers for the prompts that put data in elements (answers, rewrite, judge): an attribute
value that nothing inside it can close, and the short ids the model cites excerpts by."""

from collections.abc import Sequence
from xml.sax.saxutils import escape

# Escaped in an attribute value besides &, < and >: the double quote that closes it, and line
# breaks and tabs, so a value (a document's title is the uploader's) stays on its element's line.
_ATTRIBUTE_ENTITIES = {'"': "&quot;", "\n": "&#10;", "\r": "&#13;", "\t": "&#9;"}


def attribute(value: str) -> str:
    """An XML attribute value, always in double quotes, whatever it holds. (`quoteattr` switches
    to single quotes when the value holds a double quote, so the delimiter would depend on it.)"""
    return f'"{escape(value, _ATTRIBUTE_ENTITIES)}"'


def excerpt_ids[T](excerpts: Sequence[T], prefix: str = "E") -> dict[str, T]:
    """The id each excerpt gets in the prompt. The model cites these ids, never chunk UUIDs."""
    return {f"{prefix}{number}": excerpt for number, excerpt in enumerate(excerpts, start=1)}
