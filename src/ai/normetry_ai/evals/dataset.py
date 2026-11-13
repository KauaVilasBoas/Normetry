"""Check the golden dataset of regulation questions (evals/datasets/regulation-qa/items.jsonl).

    uv run python -m normetry_ai.evals.dataset    # lists every problem and exits 1 if there is one

Each item is checked against the schema in the dataset's README, and every quote must appear in
the parsed text of the section it names (manifest source id + `section_path`), after the same
normalize_text the parser applies. Fetch the corpus first (corpus/scripts/fetch_corpus.py).
"""

import json
import re
from collections import Counter
from collections.abc import Mapping
from pathlib import Path

from normetry_ai.ingestion.corpus import load_manifest, parse_corpus
from normetry_ai.ingestion.normalize import normalize_text

DATASET = (
    Path(__file__).resolve().parents[4] / "evals" / "datasets" / "regulation-qa" / "items.jsonl"
)

FIELDS = {
    "id",
    "jurisdiction",
    "language",
    "type",
    "turns",
    "expected_behavior",
    "expected_answer",
    "provisions",
}
PROVISION_FIELDS = {"source_id", "section_path", "quote"}
# The language an establishment of each jurisdiction asks in (phase 1 design, "Languages").
LANGUAGES = {"US": {"en"}, "EU": {"en", "pt"}, "BR": {"pt"}}
# The behavior each item type expects; a follow-up's last turn can go either way.
BEHAVIORS = {
    "direct": {"answer"},
    "two_provisions": {"answer"},
    "misleading_premise": {"answer"},
    "not_covered": {"refuse"},
    "wrong_jurisdiction": {"refuse"},
    "follow_up": {"answer", "refuse"},
}
QUOTE_WORDS = range(8, 61)

# (source_id, section_path) -> normalized section text, as parse_corpus() returns it.
Sections = Mapping[tuple[str, str], str]


def load_items(path: Path = DATASET) -> list[dict]:
    """The dataset's items, one JSON object per non-empty line."""
    with path.open(encoding="utf-8") as lines:
        return [json.loads(line) for line in lines if line.strip()]


def check_item(item: dict, sections: Sections, sources: Mapping[str, dict]) -> list[str]:
    """Problems with one item; `sources` maps manifest ids to their manifest entries."""
    item_id = item.get("id")
    if set(item) != FIELDS:
        return [f"{item_id}: fields must be exactly {', '.join(sorted(FIELDS))}"]
    problems: list[str] = []

    def need(ok: object, message: str) -> None:
        if not ok:
            problems.append(f"{item_id}: {message}")

    jurisdiction, language = item["jurisdiction"], item["language"]
    kind, behavior, turns = item["type"], item["expected_behavior"], item["turns"]
    need(
        isinstance(item_id, str) and re.fullmatch(rf"{str(jurisdiction).lower()}-\d{{3}}", item_id),
        "id must be the jurisdiction in lowercase and three digits, like br-001",
    )
    need(language in LANGUAGES.get(jurisdiction, ()), "jurisdiction and language do not fit")
    need(behavior in BEHAVIORS.get(kind, ()), f"type {kind!r} cannot expect {behavior!r}")
    need(
        isinstance(turns, list) and all(isinstance(t, str) and t.strip() for t in turns),
        "turns must be a list of non-empty strings",
    )
    need(
        isinstance(turns, list) and len(turns) in ((2, 3) if kind == "follow_up" else (1,)),
        "a follow_up has 2 or 3 turns, any other type exactly 1",
    )
    need(isinstance(item["expected_answer"], str) and item["expected_answer"].strip(), "no answer")

    provisions = item["provisions"]
    if not isinstance(provisions, list):
        return [*problems, f"{item_id}: provisions must be a list"]
    if behavior == "refuse":
        need(not provisions, "a refusal cites no provision")
    else:
        need(len(provisions) >= (2 if kind == "two_provisions" else 1), "too few provisions")
    for provision in provisions:
        if not isinstance(provision, dict) or set(provision) != PROVISION_FIELDS:
            need(False, f"a provision must have exactly {', '.join(sorted(PROVISION_FIELDS))}")
            continue
        source_id, path, quote = (
            provision[field] for field in ("source_id", "section_path", "quote")
        )
        source = sources.get(source_id)
        need(
            source and (source["jurisdiction"], source["language"]) == (jurisdiction, language),
            f"{source_id} is not a source of the item's jurisdiction and language",
        )
        need(
            isinstance(quote, str) and len(quote.split()) in QUOTE_WORDS,
            f"quote in {path} must have 8 to 60 words",
        )
        text = sections.get((source_id, path))
        if text is None:
            need(False, f"no section {path!r} in {source_id}")
        elif isinstance(quote, str):
            need(normalize_text(quote) in text, f"quote not found in {source_id} {path}: {quote}")
    return problems


def check_items(items: list[dict], sections: Sections, sources: Mapping[str, dict]) -> list[str]:
    """Problems with every item, plus duplicate ids."""
    problems = [problem for item in items for problem in check_item(item, sections, sources)]
    ids = Counter(item.get("id") for item in items)
    return problems + [f"{item_id}: duplicate id" for item_id, count in ids.items() if count > 1]


def main() -> None:
    """Check the dataset against the parsed corpus and print its counts."""
    items = load_items()
    sections = {(s.source_id, s.section_path): s.text for s in parse_corpus()}
    sources = {source["id"]: source for source in load_manifest()}
    problems = check_items(items, sections, sources)
    for problem in problems:
        print(problem)
    counts = Counter(f"{i.get('jurisdiction')} {i.get('language')} {i.get('type')}" for i in items)
    for key, count in sorted(counts.items()):
        print(f"{count:4}  {key}")
    quotes = sum(len(item.get("provisions") or ()) for item in items)
    print(f"{len(items)} items, {quotes} quotes, {len(problems)} problems")
    if problems:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
