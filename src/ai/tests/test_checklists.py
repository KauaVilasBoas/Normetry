"""The inspection checklists (normetry_ai/inspection/checklists/*.json).

The validation rules run offline on in-memory checklists. The corpus test needs the fetched corpus
(`uv run corpus/scripts/fetch_corpus.py` from the repository root): without it it is skipped
locally; in CI (`CI` set) it fails.
"""

import copy

import pytest
from test_corpus import _parse

from normetry_ai.ingestion.corpus import load_manifest
from normetry_ai.inspection import checklist
from normetry_ai.inspection.checklist import (
    CHECKLIST_CODES,
    THEMES,
    Checklist,
    ChecklistItem,
    items_for,
    load_checklist,
    parse_checklist,
)

JURISDICTIONS = list(CHECKLIST_CODES)


def _item(number: int, **changes: object) -> dict:
    return {
        "id": f"us.item-{number}",
        "theme": THEMES[number % len(THEMES)],
        "title": "Cold-held TCS food is kept at 5°C (41°F) or less",
        "section_paths": ["3-501.16"],
        "severity": "critical",
        "applies_to": ["Restaurant"],
        "evidence_hint": "cold holding temperature logs",
        **changes,
    }


# A valid US checklist: 30 items, every theme covered.
VALID = {"code": "food-code", "jurisdiction": "US", "items": [_item(n) for n in range(30)]}


def _with(*, items: list[dict] | None = None, first: dict | None = None, **fields: object) -> dict:
    """VALID with top-level fields replaced, its items replaced, or its first item changed."""
    data = {**copy.deepcopy(VALID), **fields}
    if items is not None:
        data["items"] = items
    if first is not None:
        data["items"][0] = {**data["items"][0], **first}
    return data


def test_a_valid_checklist_parses_into_frozen_items() -> None:
    parsed = parse_checklist(VALID, "US")

    assert (parsed.code, parsed.jurisdiction, len(parsed.items)) == ("food-code", "US", 30)
    assert parsed.items[0] == ChecklistItem(
        id="us.item-0",
        theme="personal_hygiene",
        title="Cold-held TCS food is kept at 5°C (41°F) or less",
        section_paths=("3-501.16",),
        severity="critical",
        applies_to=("Restaurant",),
        evidence_hint="cold holding temperature logs",
    )
    assert parse_checklist(_with(items=[_item(n) for n in range(50)]), "US").items[-1].id == (
        "us.item-49"
    )
    assert parse_checklist(_with(first={"title": "x" * 100}), "US").items[0].title == "x" * 100


@pytest.mark.parametrize(
    ("data", "problem"),
    [
        (_with(code="reg-852-2004"), "must be 'food-code' and 'US'"),
        (_with(jurisdiction="EU"), "must be 'food-code' and 'US'"),
        (_with(version="1"), "exactly code, jurisdiction and items"),
        (_with(items={}), "exactly code, jurisdiction and items"),
        (_with(first={"theme": "noise"}), "us.item-0: unknown theme 'noise'"),
        (_with(first={"severity": "priority"}), "us.item-0: unknown severity 'priority'"),
        (_with(first={"id": "us.item-1"}), "us.item-1: duplicate id"),
        (_with(items=[_item(n) for n in range(29)]), "29 items, must be 30 to 50"),
        (_with(items=[_item(n) for n in range(51)]), "51 items, must be 30 to 50"),
        (
            _with(items=[_item(n, theme="waste") for n in range(30)]),
            "no item on theme pest_control",
        ),
        (_with(first={"id": "eu.cold-holding"}), "id must be 'us.' and a kebab-case slug"),
        (_with(first={"id": "us.Cold-holding"}), "id must be"),
        (_with(first={"id": "us.cold_holding"}), "id must be"),
        (_with(first={"id": "us.cold--holding"}), "id must be"),
        (_with(first={"id": "us.-cold"}), "id must be"),
        (_with(first={"id": "us."}), "id must be"),
        (_with(first={"id": "us-cold"}), "id must be"),
        (_with(first={"id": 7}), "id must be"),
        (_with(first={"title": ""}), "us.item-0: title is empty"),
        (_with(first={"title": "   "}), "us.item-0: title is empty"),
        (_with(first={"title": "x" * 101}), "us.item-0: title is over 100 characters"),
        (_with(first={"section_paths": []}), "us.item-0: section_paths must be a non-empty list"),
        (_with(first={"section_paths": "3-501.16"}), "section_paths must be a non-empty list"),
        (_with(first={"section_paths": [""]}), "section_paths must be a non-empty list"),
        (_with(first={"applies_to": []}), "us.item-0: applies_to must be a non-empty list"),
        (_with(first={"evidence_hint": ""}), "us.item-0: evidence_hint is empty"),
        (_with(first={"note": "extra"}), "an item must have exactly the fields"),
    ],
)
def test_a_broken_checklist_is_refused(data: dict, problem: str) -> None:
    with pytest.raises(ValueError, match="food-code: ") as refused:
        parse_checklist(data, "US")

    assert problem in str(refused.value)


def test_an_item_with_a_missing_field_is_refused() -> None:
    item = _item(0)
    del item["evidence_hint"]

    with pytest.raises(ValueError, match="an item must have exactly the fields"):
        parse_checklist(_with(items=[item, *VALID["items"][1:]]), "US")


def test_every_problem_is_listed_at_once() -> None:
    with pytest.raises(ValueError) as refused:
        parse_checklist(_with(first={"theme": "noise", "severity": "low"}), "US")

    assert "unknown theme 'noise'" in str(refused.value)
    assert "unknown severity 'low'" in str(refused.value)


@pytest.mark.parametrize("jurisdiction", ["CA", "us", ""])
def test_an_unknown_jurisdiction_is_refused(jurisdiction: str) -> None:
    with pytest.raises(ValueError, match="no checklist for jurisdiction"):
        parse_checklist(VALID, jurisdiction)
    with pytest.raises(ValueError, match="no checklist for jurisdiction"):
        load_checklist(jurisdiction)


def test_items_for_groups_by_theme_in_order_and_filters_by_type(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def item(number: int, theme: str, *applies_to: str) -> ChecklistItem:
        return ChecklistItem(
            f"us.item-{number}", theme, "t", ("3-501.16",), "minor", applies_to, ""
        )

    fake = Checklist(
        "food-code",
        "US",
        (
            item(1, "waste", "Restaurant", "Bakery"),
            item(2, "personal_hygiene", "Restaurant"),
            item(3, "waste", "Restaurant"),
            item(4, "pest_control", "Bakery"),
        ),
    )
    monkeypatch.setattr(checklist, "load_checklist", lambda jurisdiction: fake)

    restaurant = items_for("US", "Restaurant")
    bakery = items_for("US", "Bakery")

    assert {theme: [i.id for i in grouped] for theme, grouped in restaurant.items()} == {
        "personal_hygiene": ["us.item-2"],
        "waste": ["us.item-1", "us.item-3"],
    }
    assert list(bakery) == ["pest_control", "waste"]
    assert items_for("US", "Pharmacy") == {}


# --- The real checklists (package data) -------------------------------------------------------


@pytest.mark.parametrize("jurisdiction", JURISDICTIONS)
def test_the_checklist_loads_with_30_to_50_items_on_every_theme(jurisdiction: str) -> None:
    loaded = load_checklist(jurisdiction)

    assert (loaded.code, loaded.jurisdiction) == (CHECKLIST_CODES[jurisdiction], jurisdiction)
    assert 30 <= len(loaded.items) <= 50
    assert {item.theme for item in loaded.items} == set(THEMES)


def test_ids_are_unique_across_the_checklists() -> None:
    ids = [item.id for j in JURISDICTIONS for item in load_checklist(j).items]

    assert len(ids) == len(set(ids))


@pytest.mark.parametrize("jurisdiction", JURISDICTIONS)
def test_items_for_a_restaurant_are_grouped_by_theme(jurisdiction: str) -> None:
    grouped = items_for(jurisdiction, "Restaurant")

    assert list(grouped) == [theme for theme in THEMES if theme in grouped]
    assert all(item.theme == theme for theme, items in grouped.items() for item in items)
    assert [item for items in grouped.values() for item in items] == sorted(
        (item for item in load_checklist(jurisdiction).items if "Restaurant" in item.applies_to),
        key=lambda item: THEMES.index(item.theme),
    )
    assert items_for(jurisdiction, "Pharmacy") == {}


@pytest.mark.parametrize("jurisdiction", JURISDICTIONS)
def test_every_section_path_exists_in_every_language_version(jurisdiction: str) -> None:
    code = CHECKLIST_CODES[jurisdiction]
    sources = [source["id"] for source in load_manifest() if source["code"] == code]
    assert sources

    missing = []
    for source_id in sources:
        paths = {section.section_path for section in _parse(source_id)}
        missing += [
            f"{item.id}: {path} not in {source_id}"
            for item in load_checklist(jurisdiction).items
            for path in item.section_paths
            if path not in paths
        ]

    assert missing == []
