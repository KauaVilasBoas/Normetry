"""What the eval reports share: fractions, money, the success measures table and folded text."""

from collections.abc import Iterable
from decimal import Decimal

from normetry_ai.ingestion.normalize import normalize_text

# A measure with nothing to measure: an empty fraction, no cost, no time.
NOT_AVAILABLE = "n/a"


def fraction(value: tuple[int, int]) -> str:
    """`(n, total)` as "n/total (percent)", or NOT_AVAILABLE when the total is 0."""
    n, total = value
    return f"{n}/{total} ({n / total:.0%})" if total else NOT_AVAILABLE


def usd(value: Decimal | None) -> str:
    return NOT_AVAILABLE if value is None else f"US$ {value:.4f}"


def measures_table(rows: Iterable[tuple[str, object, object]]) -> list[str]:
    """The `| measure | value | target |` table of (label, value, target) rows."""
    return [
        "| measure | value | target |",
        "|---|---|---|",
        *(f"| {label} | {value} | {target} |" for label, value, target in rows),
    ]


def folded(value: str) -> str:
    """`value` after normalize_text, case-folded: how a banned string is matched."""
    return normalize_text(value).casefold()
