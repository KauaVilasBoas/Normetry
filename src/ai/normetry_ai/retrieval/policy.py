"""What a question may be answered from (phase 1 design, "Jurisdiction scope" and "Languages").

`jurisdictions_for` is the only code that builds the jurisdiction list. Retrieval accepts nothing
else, so comparing jurisdictions later is a change to this function (plus labels on citations and
evals). `corpus_versions` decides which language version of each jurisdiction's corpus is searched.
"""

from dataclasses import dataclass

# The language versions of each jurisdiction's corpus (corpus/manifest.json).
CORPUS_LANGUAGES = {"US": ("en",), "EU": ("en", "pt"), "BR": ("pt",)}
JURISDICTIONS = frozenset(CORPUS_LANGUAGES)


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


def corpus_versions(scope: JurisdictionScope, language: str) -> list[tuple[str, str]]:
    """The (jurisdiction, language) pairs to search for a question in `language`.

    A jurisdiction with a version in the question's language is searched in that version only, so
    an EU provision never comes back twice. One without it (a BR or US question in the other
    language) is searched whole: the embedding model is multilingual.
    """
    return [
        (jurisdiction, version)
        for jurisdiction in scope.jurisdictions
        for version in (
            (language,)
            if language in CORPUS_LANGUAGES[jurisdiction]
            else CORPUS_LANGUAGES[jurisdiction]
        )
    ]
