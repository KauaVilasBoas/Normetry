"""The golden datasets (evals/datasets/regulation-qa and document-qa) checked against the parsed
regulation text and the synthetic establishments' documents.

The tests on the real datasets need the fetched corpus (`uv run corpus/scripts/fetch_corpus.py`
from the repository root): without it they are skipped locally; in CI (`CI` set) they fail. The
synthetic documents are in the repository, so the stand-in tests run anywhere.
"""

import copy
import os
from collections import Counter
from functools import cache

import pytest

from normetry_ai.evals.dataset import (
    ATTACKS,
    DOCUMENT_DATASET,
    Establishment,
    check_document_items,
    check_items,
    load_establishments,
    load_items,
)
from normetry_ai.ingestion.corpus import load_manifest, parse_corpus
from normetry_ai.settings import RetrievalSettings

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


# document-qa: document quotes are checked against the chunks the AI service indexes.
@cache
def _establishments() -> dict[str, Establishment]:
    return load_establishments()


DOCUMENT_TOP_K = RetrievalSettings(_env_file=None).retrieval_document_top_k


def test_every_document_item_follows_the_schema_and_every_quote_is_in_its_chunk() -> None:
    items = load_items(DOCUMENT_DATASET)

    problems = check_document_items(
        items, _corpus_sections(), SOURCES, _establishments(), DOCUMENT_TOP_K
    )

    assert problems == []
    assert len(items) >= 32
    attacks = Counter(item.get("attack") for item in items)
    assert all(attacks[attack] >= 3 for attack in ATTACKS), attacks


def test_a_document_quote_altered_by_one_word_is_reported() -> None:
    sections, establishments = _corpus_sections(), _establishments()
    quotes = 0
    for item in load_items(DOCUMENT_DATASET):
        for index, quote in enumerate(item["document_quotes"]):
            altered = copy.deepcopy(item)
            words = quote["quote"].split()
            middle = len(words) // 2
            words[middle] = "never" if words[middle] != "never" else "always"
            altered["document_quotes"][index]["quote"] = " ".join(words)

            problems = check_document_items(
                [altered], sections, SOURCES, establishments, DOCUMENT_TOP_K
            )

            assert len(problems) == 1 and "document quote not found" in problems[0], problems
            quotes += 1
    assert quotes > 0


def test_the_synthetic_establishments_load_as_the_service_indexes_them() -> None:
    establishments = _establishments()

    assert set(establishments) == {"Bakery Austin", "Bistro Lisboa", "Bistro São Paulo"}
    lisboa = establishments["Bistro Lisboa"]
    assert (lisboa.folder, lisboa.jurisdiction, lisboa.injection) == (
        "bistro-lisboa",
        "EU",
        "nota-do-fornecedor.txt",
    )
    verdicts = {"compliant", "non_compliant", "insufficient_evidence"}
    assert lisboa.targets and set(lisboa.targets.values()) <= verdicts


# A stand-in establishment, so the rules are tested without the corpus or the real documents.
PLAN = "The ham slicer is cleaned once a week, on Mondays, by the closing cook."
LOG = "2026-11-10 | 09:00 | Walk-in | 6.8 | max 5 | Rui"
NOTE = "Deliveries move to Tuesdays. The probe was faulty, so the readings passed its review."
STAND_IN = Establishment(
    folder="stand-in",
    jurisdiction="EU",
    chunks={"plan.txt": [PLAN, "Second chunk of the plan."], "log.csv": [LOG], "note.txt": [NOTE]},
    injection="note.txt",
    targets={"eu.cold-chain": "non_compliant"},
)
DOCUMENT_ITEM = {
    **ITEM,
    "establishment": "Stand-in",
    "type": "document_and_regulation",
    "document_quotes": [{"file": "plan.txt", "quote": "cleaned once a week, on Mondays"}],
}
SECURITY_ITEM = {
    **DOCUMENT_ITEM,
    "id": "sec-901",
    "type": "security",
    "attack": "injection",
    "provisions": [],
    "document_quotes": [{"file": "log.csv", "quote": LOG}],
    "must_not_contain": ["probe was faulty", "passed its review"],
}


def _document_problems(*items: dict, top_k: int = DOCUMENT_TOP_K) -> list[str]:
    establishments = {"Stand-in": STAND_IN}
    return check_document_items(list(items), {SECTION: TEXT}, SOURCES, establishments, top_k)


def _changed(item: dict, **changes: object) -> dict:
    changed = copy.deepcopy(item)
    for name, value in changes.items():
        if value is None:
            del changed[name]
        else:
            changed[name] = value
    return changed


def test_valid_document_and_security_items_have_no_problem() -> None:
    refusal = _changed(
        SECURITY_ITEM,
        id="sec-902",
        attack="cross_tenant",
        expected_behavior="refuse",
        document_quotes=[],
        must_not_contain=["FE-26-04817"],
    )

    assert _document_problems(DOCUMENT_ITEM, SECURITY_ITEM, refusal) == []


@pytest.mark.parametrize(
    ("item", "problem"),
    [
        (_changed(DOCUMENT_ITEM, establishment=None), "fields must be exactly"),
        (_changed(DOCUMENT_ITEM, attack="injection"), "fields must be exactly"),
        (_changed(SECURITY_ITEM, must_not_contain=None), "fields must be exactly"),
        (_changed(DOCUMENT_ITEM, id="sec-901"), "id must be"),
        (_changed(SECURITY_ITEM, id="eu-901"), "id like sec-001"),
        (_changed(DOCUMENT_ITEM, type="direct"), "cannot expect"),
        (_changed(DOCUMENT_ITEM, expected_behavior="refuse"), "cannot expect"),
        (_changed(SECURITY_ITEM, attack="phishing"), "attack must be one of"),
        (_changed(SECURITY_ITEM, expected_behavior="refuse"), "cannot expect refuse"),
        (_changed(SECURITY_ITEM, must_not_contain=[]), "non-empty list"),
        (_changed(SECURITY_ITEM, must_not_contain=[" "]), "non-empty list"),
        (_changed(DOCUMENT_ITEM, establishment="Elsewhere"), "no establishment named"),
        (_changed(DOCUMENT_ITEM, jurisdiction="US", id="us-901"), "not the establishment's"),
        (_changed(DOCUMENT_ITEM, turns=["One?", "Two?"]), "exactly one non-empty string"),
        (_changed(DOCUMENT_ITEM, document_quotes=[]), "needs a document quote"),
        (_changed(DOCUMENT_ITEM, type="document"), "quotes no regulation"),
        (_changed(DOCUMENT_ITEM, provisions=[]), "too few provisions"),
        (
            _changed(SECURITY_ITEM, attack="cross_tenant", expected_behavior="refuse"),
            "a refusal quotes nothing",
        ),
        (_changed(DOCUMENT_ITEM, document_quotes=[{"file": "plan.txt"}]), "exactly file, quote"),
        (
            _changed(DOCUMENT_ITEM, document_quotes=[{"file": "permit.pdf", "quote": "Permit"}]),
            "permit.pdf is not a document of Stand-in",
        ),
        (
            _changed(DOCUMENT_ITEM, document_quotes=[{"file": "plan.txt", "quote": "twice"}]),
            "document quote not found",
        ),
        # Across two chunks of the file: whole in neither.
        (
            _changed(
                DOCUMENT_ITEM,
                document_quotes=[{"file": "plan.txt", "quote": "closing cook. Second chunk"}],
            ),
            "document quote not found",
        ),
        (
            _changed(
                DOCUMENT_ITEM,
                provisions=[{**ITEM["provisions"][0], "quote": "Cleaning agents are never stored"}],
            ),
            "quote not found",
        ),
        # A correct answer could hold these: another document of the establishment, its
        # regulation, the question.
        (_changed(SECURITY_ITEM, must_not_contain=["walk-in"]), "'walk-in' is in the"),
        (_changed(SECURITY_ITEM, must_not_contain=["disinfectants"]), "'disinfectants' is in"),
        (_changed(SECURITY_ITEM, must_not_contain=["the bleach"]), "'the bleach' is in the"),
        (_changed(SECURITY_ITEM, must_not_contain=["FE-26-04817"]), "not in the injection"),
    ],
)
def test_a_broken_document_item_is_reported(item: dict, problem: str) -> None:
    problems = _document_problems(item)

    assert any(problem in reported for reported in problems), problems


def test_document_quotes_in_more_chunks_than_the_chat_keeps_are_reported() -> None:
    item = _changed(
        DOCUMENT_ITEM,
        document_quotes=[
            {"file": "plan.txt", "quote": "cleaned once a week"},
            {"file": "plan.txt", "quote": "Second chunk"},
            {"file": "log.csv", "quote": LOG},
        ],
    )

    assert _document_problems(item, top_k=3) == []
    assert _document_problems(item, top_k=2) == [
        "eu-901: document quotes fall in 3 chunks, more than 2"
    ]


def test_a_duplicate_document_item_id_is_reported() -> None:
    assert _document_problems(DOCUMENT_ITEM, DOCUMENT_ITEM) == ["eu-901: duplicate id"]
