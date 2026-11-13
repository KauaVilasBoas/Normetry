"""The corpus parser on the real, pinned regulation files (corpus/manifest.json).

The FDA and Anvisa files are not in git: fetch them with `uv run corpus/scripts/fetch_corpus.py`
from the repository root. Without them these tests are skipped locally; in CI (`CI` set) they fail.
"""

import os
import re
from functools import cache

import pytest

from normetry_ai.ingestion.chunking import CHUNK_LIMIT, chunk_sections
from normetry_ai.ingestion.corpus import Section, load_manifest, parse_source
from normetry_ai.ingestion.normalize import normalize_text

FDA = "us-fda-food-code-2026"
EU_EN = "eu-reg-852-2004-en"
EU_PT = "eu-reg-852-2004-pt"
RDC = "br-anvisa-rdc-216-2004"


@cache
def _parse(source_id: str) -> tuple[Section, ...]:
    source = next(source for source in load_manifest() if source["id"] == source_id)
    try:
        return tuple(parse_source(source))
    except FileNotFoundError:
        if os.environ.get("CI"):
            raise
        pytest.skip(
            "Corpus not fetched: run `uv run corpus/scripts/fetch_corpus.py` from the root."
        )


def texts(source_id: str) -> dict[str, str]:
    return {section.section_path: section.text for section in _parse(source_id)}


def test_normalize_joins_line_break_hyphenation_and_collapses_whitespace() -> None:
    assert normalize_text(" HACCP prin￾ciples,\r\n  together ") == "HACCP principles, together"
    assert normalize_text("the prin-\n  ciples\xa0of 57°C\t(135°F)") == (
        "the principles of 57°C (135°F)"
    )
    # Only a hyphen at a line end before a lowercase letter is hyphenation.
    assert normalize_text("higiênico- sanitários, 3-501.16, Non-\nCompliance") == (
        "higiênico- sanitários, 3-501.16, Non- Compliance"
    )
    assert normalize_text("à") == "à"


@pytest.mark.parametrize(
    ("source_id", "section_path", "opening"),
    [
        (
            FDA,
            "3-501.16",
            "Time/Temperature Control for Safety Food, Hot and Cold Holding. (A) Except during "
            "preparation, cooking, or cooling, or when time is used as the public health control",
        ),
        (FDA, "1-101.10", "Food Code. These provisions shall be known as the “Food Code,”"),
        (FDA, "4-202.13", '"V" Threads, Use Limitation. Except for hot oil cooking'),
        (FDA, "4-1001.11", "Food-Contact, NonFood-Contact Surfaces, and Utensils *EQUIPMENT"),
        (
            EU_EN,
            "Article 4",
            "General and specific hygiene requirements 1. Food business operators carrying out "
            "primary production and those associated operations listed in Annex I shall comply",
        ),
        (
            EU_EN,
            "Annex II, Chapter I",
            "General requirements for food premises (other than those specified in chapter iii) "
            "1. Food premises are to be kept clean and maintained in good repair and condition.",
        ),
        (
            EU_PT,
            "Article 4",
            "Requisitos gerais e específicos de higiene 1. Os operadores das empresas do sector "
            "alimentar que se dediquem à produção primária",
        ),
        (
            EU_PT,
            "Annex II, Chapter I",
            "Requisitos gerais aplicáveis às instalações do sector alimentar (com excepção das "
            "especificadas no capítulo III) 1. As instalações do sector alimentar devem ser "
            "mantidas limpas",
        ),
        (
            RDC,
            "4.1.1",
            "A edificação e as instalações devem ser projetadas de forma a possibilitar um fluxo "
            "ordenado e sem cruzamentos",
        ),
        (
            RDC,
            "4.8.15",
            "Após serem submetidos à cocção, os alimentos preparados devem ser mantidos em "
            "condições de tempo e de temperatura que não favoreçam a multiplicação microbiana.",
        ),
    ],
)
def test_known_section_has_its_path_and_opening_text(
    source_id: str, section_path: str, opening: str
) -> None:
    assert texts(source_id)[section_path].startswith(opening)


def test_every_section_carries_its_manifest_metadata() -> None:
    for source in load_manifest():
        for section in _parse(source["id"]):
            assert (section.jurisdiction, section.language, section.version) == (
                source["jurisdiction"],
                source["language"],
                source["version"],
            )
            assert section.text == normalize_text(section.text)


def test_fda_keeps_chapters_1_to_8_only() -> None:
    sections = texts(FDA)
    paths = list(sections)

    assert all(re.fullmatch(r"[1-8]-\d{3,4}\.\d{2}", path) for path in paths)
    assert paths[0] == "1-101.10"
    assert paths[-1] == "8-501.40"
    # Annex 1 continues chapter 8's numbering; none of it is a code section here.
    assert not any(path.startswith(("8-6", "8-7", "8-8", "8-9")) for path in paths)
    every_text = " ".join(sections.values())
    for annex_text in (
        "Abdul-Raouf, U.M.",  # Annex 2, references for 3-501.16
        "Public Health Reasons",  # Annex 3
        "FDA Food Code 2026",  # running headers
        "Subparts:",
    ):
        assert annex_text not in every_text
    # Set without hyphenation: line-end hyphens stay ("earliest-prepared", not "earliestprepared").
    assert "earliest-prepared" in sections["3-501.17"]


def test_eu_keeps_articles_and_annex_parts_and_chapters() -> None:
    en, pt = texts(EU_EN), texts(EU_PT)

    assert list(en) == list(pt)  # same provisions, same paths, same order in both languages
    articles = [path for path in en if path.startswith("Article")]
    assert articles == [f"Article {number}" for number in range(1, 19)]
    assert [path for path in en if path.startswith("Annex I,")] == [
        "Annex I, Part A",
        "Annex I, Part B",
    ]
    assert "Annex II, Chapter Va" in en and "Annex II, Chapter XIa" in en
    every_text = " ".join([*en.values(), *pt.values()])
    # The consolidated text has no recitals; the cover note, page headers, consolidation markers
    # and footnotes ("(1) OJ L 330, ..." under Article 2) are not provision text either.
    for excluded in ("Whereas", "Considerando", "documentation tool", "02004R0852", "OJ L 330"):
        assert excluded not in every_text
    assert not re.search(r"[►▼◄]", every_text)
    assert "(1) Food business operators shall routinely check" in en["Annex II, Chapter Va"]


def test_rdc_keeps_the_technical_regulation_items_only() -> None:
    sections = texts(RDC)
    paths = list(sections)

    assert all(re.fullmatch(r"[1-4](\.\d+)*", path) for path in paths)
    assert paths[0] == "1.1"
    assert paths[-1] == "4.12.2"
    every_text = " ".join(sections.values())
    for excluded in (
        "Aprovar o Regulamento Técnico",  # art. 1 of the resolution (preamble)
        "Fica revogada a Resolução CNNPA",  # art. 5
        "considerando a necessidade",
        "Redações Anteriores",  # the site's link to superseded wording
        "Este texto não substitui",
    ):
        assert excluded not in every_text
    # A bare heading has no provision of its own.
    assert "4.1" not in sections


@pytest.mark.parametrize("source_id", [FDA, EU_EN, EU_PT, RDC])
def test_paths_are_unique_and_texts_are_not_empty(source_id: str) -> None:
    sections = _parse(source_id)

    assert len({section.section_path for section in sections}) == len(sections)
    assert all(section.text for section in sections)


@pytest.mark.parametrize("source_id", [FDA, EU_EN, EU_PT, RDC])
def test_parsing_twice_gives_the_same_sections(source_id: str) -> None:
    first = _parse(source_id)
    source = next(source for source in load_manifest() if source["id"] == source_id)

    assert tuple(parse_source(source)) == first


@pytest.mark.parametrize("source_id", [FDA, EU_EN, EU_PT, RDC])
def test_chunks_fit_the_limit_and_join_back_into_their_sections(source_id: str) -> None:
    sections = _parse(source_id)
    chunks = chunk_sections(sections)
    by_path: dict[str, list[str]] = {}
    for chunk in chunks:
        by_path.setdefault(chunk.section_path, []).append(chunk.content)

    assert all(len(chunk.content) <= CHUNK_LIMIT for chunk in chunks)
    assert {path: " ".join(parts) for path, parts in by_path.items()} == {
        section.section_path: section.text for section in sections
    }
    # No chunk is a stray FDA priority tag cut off from its provision.
    assert not [chunk for chunk in chunks if chunk.content in ("P", "Pf", "C")]


def test_a_file_that_does_not_match_the_manifest_is_refused() -> None:
    source = next(source for source in load_manifest() if source["id"] == EU_EN)

    with pytest.raises(ValueError, match="SHA-256"):
        parse_source({**source, "sha256": "0" * 64})
