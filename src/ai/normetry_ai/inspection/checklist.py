"""Inspection checklists: the key requirements of each regulation, shipped as package data.

One JSON file per corpus code in `checklists/` (contract:
specs/001-checklists-synthetic-establishments/contracts/checklist-file.md). `load_checklist`
validates the file and raises ValueError listing every problem; `items_for` gives an inspection
the items for one establishment type, grouped by theme. That every `section_path` exists in the
parsed corpus is checked by tests/test_checklists.py, which needs the fetched corpus.
"""

import json
import re
from collections import Counter
from dataclasses import dataclass
from functools import cache
from importlib.resources import files

THEMES = (
    "personal_hygiene",
    "temperature_control",
    "cross_contamination_allergens",
    "cleaning_sanitizing",
    "pest_control",
    "water_premises",
    "waste",
    "documentation_training",
)
SEVERITIES = ("critical", "major", "minor")
# Jurisdiction -> corpus code (corpus/manifest.json); the file is checklists/<code>.json.
CHECKLIST_CODES = {"US": "food-code", "EU": "reg-852-2004", "BR": "rdc-216-2004"}
ITEM_COUNT = range(30, 51)
TITLE_LIMIT = 100

FIELDS = {"code", "jurisdiction", "items"}
ITEM_FIELDS = {
    "id",
    "theme",
    "title",
    "section_paths",
    "severity",
    "applies_to",
    "evidence_hint",
}


@dataclass(frozen=True)
class ChecklistItem:
    """One key requirement: what to check, where the regulation says it, what records show it."""

    id: str
    theme: str
    title: str
    section_paths: tuple[str, ...]
    severity: str
    applies_to: tuple[str, ...]
    evidence_hint: str


@dataclass(frozen=True)
class Checklist:
    code: str
    jurisdiction: str
    items: tuple[ChecklistItem, ...]


def _strings(value: object) -> bool:
    """A non-empty list of non-blank strings."""
    return (
        isinstance(value, list)
        and bool(value)
        and all(isinstance(entry, str) and entry.strip() for entry in value)
    )


def _item_problems(item: object, jurisdiction: str) -> list[str]:
    if not isinstance(item, dict) or set(item) != ITEM_FIELDS:
        return [f"an item must have exactly the fields {', '.join(sorted(ITEM_FIELDS))}"]
    item_id = item["id"]
    rules = [
        (
            isinstance(item_id, str)
            and re.fullmatch(
                rf"{re.escape(jurisdiction.lower())}\.[a-z0-9]+(-[a-z0-9]+)*", item_id
            ),
            f"id must be '{jurisdiction.lower()}.' and a kebab-case slug",
        ),
        (item["theme"] in THEMES, f"unknown theme {item['theme']!r}"),
        (item["severity"] in SEVERITIES, f"unknown severity {item['severity']!r}"),
        (isinstance(item["title"], str) and item["title"].strip(), "title is empty"),
        (
            not isinstance(item["title"], str) or len(item["title"]) <= TITLE_LIMIT,
            f"title is over {TITLE_LIMIT} characters",
        ),
        (_strings(item["section_paths"]), "section_paths must be a non-empty list of strings"),
        (_strings(item["applies_to"]), "applies_to must be a non-empty list of strings"),
        (
            isinstance(item["evidence_hint"], str) and item["evidence_hint"].strip(),
            "evidence_hint is empty",
        ),
    ]
    return [f"{item_id}: {message}" for ok, message in rules if not ok]


def _code(jurisdiction: str) -> str:
    try:
        return CHECKLIST_CODES[jurisdiction]
    except KeyError:
        raise ValueError(
            f"no checklist for jurisdiction {jurisdiction!r}: one of {', '.join(CHECKLIST_CODES)}"
        ) from None


def parse_checklist(data: object, jurisdiction: str) -> Checklist:
    """The checklist of `jurisdiction` from its file's JSON; ValueError lists every problem."""
    code = _code(jurisdiction)
    if not isinstance(data, dict) or set(data) != FIELDS or not isinstance(data["items"], list):
        raise ValueError(f"{code}: must be an object with exactly code, jurisdiction and items")
    items = data["items"]
    problems = [problem for item in items for problem in _item_problems(item, jurisdiction)]
    if data["code"] != code or data["jurisdiction"] != jurisdiction:
        problems.append(f"code and jurisdiction must be {code!r} and {jurisdiction!r}")
    if len(items) not in ITEM_COUNT:
        problems.append(f"{len(items)} items, must be 30 to 50")
    records = [item for item in items if isinstance(item, dict)]
    ids = Counter(item.get("id") for item in records if isinstance(item.get("id"), str))
    problems += [f"{item_id}: duplicate id" for item_id, count in ids.items() if count > 1]
    themes = [item.get("theme") for item in records]
    problems += [f"no item on theme {theme}" for theme in THEMES if theme not in themes]
    if problems:
        raise ValueError(f"{code}: " + "; ".join(problems))
    return Checklist(
        code=code,
        jurisdiction=jurisdiction,
        items=tuple(
            ChecklistItem(
                id=item["id"],
                theme=item["theme"],
                title=item["title"],
                section_paths=tuple(item["section_paths"]),
                severity=item["severity"],
                applies_to=tuple(item["applies_to"]),
                evidence_hint=item["evidence_hint"],
            )
            for item in items
        ),
    )


@cache
def load_checklist(jurisdiction: str) -> Checklist:
    """The validated checklist of `jurisdiction` (US, EU or BR), read from the package data."""
    path = files(__package__) / "checklists" / f"{_code(jurisdiction)}.json"
    return parse_checklist(json.loads(path.read_text(encoding="utf-8")), jurisdiction)


def items_for(jurisdiction: str, establishment_type: str) -> dict[str, list[ChecklistItem]]:
    """Items that apply to `establishment_type`, by theme in THEMES order; empty themes left out."""
    grouped: dict[str, list[ChecklistItem]] = {theme: [] for theme in THEMES}
    for item in load_checklist(jurisdiction).items:
        if establishment_type in item.applies_to:
            grouped[item.theme].append(item)
    return {theme: items for theme, items in grouped.items() if items}
