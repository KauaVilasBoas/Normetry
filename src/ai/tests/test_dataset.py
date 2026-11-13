"""The golden dataset (evals/datasets/regulation-qa) checked against the parsed regulation text.

The tests on the real dataset need the fetched corpus (`uv run corpus/scripts/fetch_corpus.py`
from the repository root): without it they are skipped locally; in CI (`CI` set) they fail.
"""

import copy
import os
from functools import cache

import pytest

from normetry_ai.evals.dataset import check_items, load_items
from normetry_ai.ingestion.corpus import load_manifest, parse_corpus

SOURCES = {source["id"]: source for source in load_manifest()}


@cache
def _corpus_sections() -> dict[tuple[str, str], str]:
    try:
        return {(s.source_id, s.section_path): s.text for s in parse_corpus()}
    except FileNotFoundError:
        if os.environ.get("CI"):
            raise
        pytest.skip(
            "Corpus not fetched: run `uv run corpus/scripts/fetch_corpus.py` from the root."
        )


def test_every_item_follows_the_schema_and_every_quote_is_in_its_section() -> None:
    items = load_items()

    assert check_items(items, _corpus_sections(), SOURCES) == []
    assert len(items) >= 59


def test_a_quote_altered_by_one_word_is_reported() -> None:
    sections = _corpus_sections()
    quotes = 0
    for item in load_items():
        for index, provision in enumerate(item["provisions"]):
            altered = copy.deepcopy(item)
            words = provision["quote"].split()
            middle = len(words) // 2
            words[middle] = "never" if words[middle] != "never" else "always"
            altered["provisions"][index]["quote"] = " ".join(words)

            problems = check_items([altered], sections, SOURCES)

            assert len(problems) == 1 and "quote not found" in problems[0], problems
            quotes += 1
    assert quotes > 0


# A valid item on a stand-in section, so the schema rules are tested without the corpus.
SECTION = ("eu-reg-852-2004-en", "Annex II, Chapter I")
TEXT = "10. Cleaning agents and disinfectants are not to be stored in areas where food is handled."
ITEM = {
    "id": "eu-901",
    "jurisdiction": "EU",
    "language": "en",
    "type": "direct",
    "turns": ["Can we keep the bleach in the kitchen?"],
    "expected_behavior": "answer",
    "expected_answer": "Not in areas where food is handled.",
    "provisions": [
        {
            "source_id": SECTION[0],
            "section_path": SECTION[1],
            "quote": "Cleaning agents and disinfectants are not to be stored in areas",
        }
    ],
}


def _broken(**changes: object) -> dict:
    item = copy.deepcopy(ITEM)
    for field, value in changes.items():
        if value is None:
            del item[field]
        else:
            item[field] = value
    return item


def test_a_valid_item_has_no_problem() -> None:
    assert check_items([ITEM], {SECTION: TEXT}, SOURCES) == []


@pytest.mark.parametrize(
    ("item", "problem"),
    [
        (_broken(turns=None), "fields must be exactly"),
        (_broken(notes="extra"), "fields must be exactly"),
        (_broken(id="us-901"), "id must be"),
        (_broken(language="es"), "jurisdiction and language do not fit"),
        (_broken(type="trivia"), "cannot expect"),
        (_broken(type="not_covered"), "cannot expect"),
        (_broken(type="follow_up"), "a follow_up has 2 or 3 turns"),
        (_broken(turns=["", "And then?"], type="follow_up"), "non-empty strings"),
        (_broken(expected_answer=" "), "no answer"),
        (_broken(expected_behavior="refuse", type="follow_up", turns=["a", "b"]), "cites no"),
        (_broken(provisions=[]), "too few provisions"),
        (_broken(type="two_provisions"), "too few provisions"),
        (_broken(language="pt"), "not a source of the item's jurisdiction and language"),
        (
            _broken(provisions=[{**ITEM["provisions"][0], "source_id": "us-fda-food-code-2026"}]),
            "not a source of the item's jurisdiction and language",
        ),
        (
            _broken(provisions=[{**ITEM["provisions"][0], "section_path": "Annex II, Chapter X"}]),
            "no section",
        ),
        (_broken(provisions=[{**ITEM["provisions"][0], "quote": "are not to be"}]), "8 to 60"),
        (
            _broken(
                provisions=[
                    {
                        **ITEM["provisions"][0],
                        "quote": "Cleaning agents and disinfectants are never to be stored in",
                    }
                ]
            ),
            "quote not found",
        ),
    ],
)
def test_a_broken_item_is_reported(item: dict, problem: str) -> None:
    problems = check_items([item], {SECTION: TEXT}, SOURCES)

    assert any(problem in reported for reported in problems), problems


def test_a_duplicate_id_is_reported() -> None:
    assert check_items([ITEM, ITEM], {SECTION: TEXT}, SOURCES) == ["eu-901: duplicate id"]
