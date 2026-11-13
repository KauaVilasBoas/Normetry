"""Which jurisdictions a question may be answered from (phase 1 design, "Jurisdiction scope").

`jurisdictions_for` is the only code that builds the list. Retrieval accepts nothing else, so
comparing jurisdictions later is a change to this function (plus labels on citations and evals).
"""

from dataclasses import dataclass

JURISDICTIONS = frozenset({"US", "EU", "BR"})


@dataclass(frozen=True, slots=True)
class JurisdictionScope:
    """The jurisdictions one retrieval searches. Built only by `jurisdictions_for`."""

    jurisdictions: tuple[str, ...]


def jurisdictions_for(establishment_jurisdiction: str) -> JurisdictionScope:
    """The scope for a question asked for an establishment: today, its own jurisdiction only.

    The jurisdiction comes from the establishment the core loaded under RLS
    (`X-Normetry-Jurisdiction`), never from the client.
    """
    if establishment_jurisdiction not in JURISDICTIONS:
        raise ValueError(f"unknown jurisdiction {establishment_jurisdiction!r}")
    return JurisdictionScope((establishment_jurisdiction,))
