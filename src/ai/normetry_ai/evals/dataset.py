"""Check the golden datasets: regulation questions (evals/datasets/regulation-qa) and document
questions with the security set (evals/datasets/document-qa).

    uv run python -m normetry_ai.evals.dataset    # lists every problem and exits 1 if there is one

Each item is checked against the schema in its dataset's README. Every regulation quote must
appear in the parsed text of the section it names (manifest source id + `section_path`), after the
same normalize_text the parser applies; every document quote in one chunk of its file as the AI
service indexes it (`parse_file`, then `chunk_sections`). Fetch the corpus first
(corpus/scripts/fetch_corpus.py).
"""

import json
import re
from collections import Counter
from collections.abc import Callable, Mapping
from dataclasses import dataclass, field
from pathlib import Path

from normetry_ai.ingestion.chunking import chunk_sections
from normetry_ai.ingestion.corpus import load_manifest, parse_corpus
from normetry_ai.ingestion.normalize import normalize_text
from normetry_ai.ingestion.parsers import CSV, DOCX, PDF, TXT, parse_file
from normetry_ai.retrieval.policy import CORPUS_LANGUAGES
from normetry_ai.settings import RetrievalSettings

EVALS = Path(__file__).resolve().parents[4] / "evals"
DATASET = EVALS / "datasets" / "regulation-qa" / "items.jsonl"
DOCUMENT_DATASET = EVALS / "datasets" / "document-qa" / "items.jsonl"
ESTABLISHMENTS = EVALS / "establishments"
CONTENT_TYPES = {".pdf": PDF, ".docx": DOCX, ".csv": CSV, ".txt": TXT}

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
# document-qa: regulation-qa's fields plus these; security items add ATTACK_FIELDS.
DOCUMENT_FIELDS = FIELDS | {"establishment", "document_quotes"}
ATTACK_FIELDS = {"attack", "must_not_contain"}
DOCUMENT_QUOTE_FIELDS = {"file", "quote"}
# The behavior each attack expects: a refusal for the cross items, the right answer otherwise.
ATTACKS = {"cross_tenant": "refuse", "cross_establishment": "refuse", "injection": "answer"}

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
    need(language in CORPUS_LANGUAGES.get(jurisdiction, ()), "jurisdiction and language do not fit")
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
    _check_provisions(provisions, jurisdiction, language, sections, sources, need)
    return problems


def _check_provisions(
    provisions: list,
    jurisdiction: str,
    language: str,
    sections: Sections,
    sources: Mapping[str, dict],
    need: Callable[[object, str], None],
) -> None:
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


def _duplicates(items: list[dict]) -> list[str]:
    ids = Counter(item.get("id") for item in items)
    return [f"{item_id}: duplicate id" for item_id, count in ids.items() if count > 1]


def check_items(items: list[dict], sections: Sections, sources: Mapping[str, dict]) -> list[str]:
    """Problems with every item, plus duplicate ids."""
    problems = [problem for item in items for problem in check_item(item, sections, sources)]
    return problems + _duplicates(items)


@dataclass(frozen=True)
class Establishment:
    """A synthetic establishment (evals/establishments/<folder>) as the AI service indexes it."""

    folder: str
    jurisdiction: str
    # File name -> the text of its chunks: `chunk_sections` over `parse_file`, in document order.
    chunks: Mapping[str, list[str]]
    # The file with instructions planted for an AI reader (expected-findings.json `injection`).
    injection: str | None = None
    # Each checklist item the injection targets -> the establishment's expected verdict for it.
    targets: Mapping[str, str] = field(default_factory=dict)


def load_establishments(root: Path = ESTABLISHMENTS) -> dict[str, Establishment]:
    """Every synthetic establishment, by its manifest name ("Bistro São Paulo")."""
    found = {}
    for folder in sorted(root.iterdir()):
        manifest_path = folder / "documents" / "manifest.json"
        if not manifest_path.is_file():
            continue
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        expected = json.loads((folder / "expected-findings.json").read_text(encoding="utf-8"))
        chunks = {}
        for entry in manifest["documents"]:
            path = folder / "documents" / entry["file"]
            parsed = parse_file(path.read_bytes(), CONTENT_TYPES[path.suffix])
            chunks[entry["file"]] = [chunk.content for chunk in chunk_sections(parsed.parts)]
        verdicts = {finding["item"]: finding["verdict"] for finding in expected["findings"]}
        injection = expected.get("injection") or {}
        found[manifest["establishment"]] = Establishment(
            folder=folder.name,
            jurisdiction=expected["jurisdiction"],
            chunks=chunks,
            injection=injection.get("file"),
            targets={item: verdicts.get(item, "?") for item in injection.get("targets", ())},
        )
    return found


def _folded(value: str) -> str:
    return normalize_text(value).casefold()


def check_document_item(
    item: dict,
    sections: Sections,
    sources: Mapping[str, dict],
    establishments: Mapping[str, Establishment],
    document_top_k: int,
) -> list[str]:
    """Problems with one document-qa item (evals/datasets/document-qa/README.md).

    Every document quote must be in one chunk of its file, and an item's quotes in at most
    `document_top_k` chunks (`retrieval_document_top_k`): the chat keeps no more document chunks
    for an answer, so a wider item could never reach full recall. A security item's
    `must_not_contain` strings must be absent from everything a correct answer may contain (the
    establishment's own documents, its regulation, the question, the expected answer, the quotes)
    and, for an injection, present in the planted document.
    """
    item_id = item.get("id")
    kind = item.get("type")
    fields = DOCUMENT_FIELDS | (ATTACK_FIELDS if kind == "security" else set())
    if set(item) != fields:
        return [f"{item_id}: fields must be exactly {', '.join(sorted(fields))}"]
    problems: list[str] = []

    def need(ok: object, message: str) -> None:
        if not ok:
            problems.append(f"{item_id}: {message}")

    jurisdiction, language = item["jurisdiction"], item["language"]
    behavior, turns = item["expected_behavior"], item["turns"]
    banned: list = []
    if kind == "security":
        need(isinstance(item_id, str) and re.fullmatch(r"sec-\d{3}", item_id), "id like sec-001")
        attack = item["attack"]
        need(attack in ATTACKS, f"attack must be one of {', '.join(ATTACKS)}")
        need(
            ATTACKS.get(attack, behavior) == behavior, f"attack {attack!r} cannot expect {behavior}"
        )
        banned = item["must_not_contain"]
        need(
            isinstance(banned, list)
            and banned
            and all(isinstance(string, str) and string.strip() for string in banned),
            "must_not_contain must be a non-empty list of non-empty strings",
        )
        banned = banned if isinstance(banned, list) else []
    else:
        need(
            isinstance(item_id, str)
            and re.fullmatch(rf"{str(jurisdiction).lower()}-\d{{3}}", item_id),
            "id must be the jurisdiction in lowercase and three digits, like br-001",
        )
        need(
            kind in ("document", "document_and_regulation") and behavior == "answer",
            f"type {kind!r} cannot expect {behavior!r}",
        )
    establishment = establishments.get(item["establishment"])
    need(establishment, f"no establishment named {item['establishment']!r}")
    need(
        establishment is None or establishment.jurisdiction == jurisdiction,
        "the jurisdiction is not the establishment's",
    )
    need(language in CORPUS_LANGUAGES.get(jurisdiction, ()), "jurisdiction and language do not fit")
    need(
        isinstance(turns, list)
        and len(turns) == 1
        and isinstance(turns[0], str)
        and turns[0].strip(),
        "turns must be exactly one non-empty string",
    )
    need(isinstance(item["expected_answer"], str) and item["expected_answer"].strip(), "no answer")

    quotes, provisions = item["document_quotes"], item["provisions"]
    if not isinstance(quotes, list) or not isinstance(provisions, list):
        return [*problems, f"{item_id}: document_quotes and provisions must be lists"]
    if behavior == "refuse":
        need(not quotes and not provisions, "a refusal quotes nothing")
    else:
        need(quotes, "an answer needs a document quote")
    if kind == "document":
        need(not provisions, "a document item quotes no regulation")
    elif kind == "document_and_regulation":
        need(provisions, "too few provisions")
    _check_provisions(provisions, jurisdiction, language, sections, sources, need)

    used: set[tuple[str, int]] = set()
    for quote in quotes:
        if not isinstance(quote, dict) or set(quote) != DOCUMENT_QUOTE_FIELDS:
            need(False, "a document quote must have exactly file, quote")
            continue
        file, text = quote["file"], quote["quote"]
        if establishment is None:
            continue
        chunks = establishment.chunks.get(file)
        if chunks is None:
            need(False, f"{file} is not a document of {item['establishment']}")
            continue
        normalized = normalize_text(text) if isinstance(text, str) else ""
        index = next(
            (i for i, chunk in enumerate(chunks) if normalized and normalized in chunk), None
        )
        if index is None:
            need(False, f"document quote not found in one chunk of {file}: {text}")
        else:
            used.add((file, index))
    need(
        len(used) <= document_top_k,
        f"document quotes fall in {len(used)} chunks, more than {document_top_k}",
    )

    if banned and establishment is not None:
        regulation = {
            source_id
            for source_id, source in sources.items()
            if (source["jurisdiction"], source["language"]) == (jurisdiction, language)
        }
        # An injection's strings come from its planted document, and only from it.
        own = {
            file: chunks
            for file, chunks in establishment.chunks.items()
            if item["attack"] != "injection" or file != establishment.injection
        }
        allowed = [
            *(chunk for chunks in own.values() for chunk in chunks),
            *(text for (source_id, _), text in sections.items() if source_id in regulation),
            *turns,
            item["expected_answer"],
            *(
                q["quote"]
                for q in quotes
                if isinstance(q, dict) and set(q) == DOCUMENT_QUOTE_FIELDS
            ),
            *(p["quote"] for p in provisions if isinstance(p, dict) and set(p) == PROVISION_FIELDS),
        ]
        allowed = [_folded(text) for text in allowed if isinstance(text, str)]
        planted = [_folded(c) for c in establishment.chunks.get(establishment.injection or "", [])]
        for string in banned:
            if not isinstance(string, str) or not string.strip():
                continue
            folded = _folded(string)
            need(
                not any(folded in text for text in allowed),
                f"{string!r} is in the establishment's own documents or regulation, the question,"
                " the expected answer or a quote",
            )
            if item["attack"] == "injection":
                need(
                    any(folded in chunk for chunk in planted),
                    f"{string!r} is not in the injection document {establishment.injection}",
                )
    return problems


def check_document_items(
    items: list[dict],
    sections: Sections,
    sources: Mapping[str, dict],
    establishments: Mapping[str, Establishment],
    document_top_k: int,
) -> list[str]:
    """Problems with every document-qa item, plus duplicate ids."""
    problems = [
        problem
        for item in items
        for problem in check_document_item(item, sections, sources, establishments, document_top_k)
    ]
    return problems + _duplicates(items)


def main() -> None:
    """Check both datasets against the parsed corpus and documents and print their counts."""
    sections = {(s.source_id, s.section_path): s.text for s in parse_corpus()}
    sources = {source["id"]: source for source in load_manifest()}
    items, documents = load_items(), load_items(DOCUMENT_DATASET)
    problems = check_items(items, sections, sources) + check_document_items(
        documents,
        sections,
        sources,
        load_establishments(),
        RetrievalSettings().retrieval_document_top_k,
    )
    for problem in problems:
        print(problem)
    counts = Counter(f"{i.get('jurisdiction')} {i.get('language')} {i.get('type')}" for i in items)
    counts.update(
        f"{i.get('establishment')}: {i.get('attack') or i.get('type')}" for i in documents
    )
    for key, count in sorted(counts.items()):
        print(f"{count:4}  {key}")
    quotes = sum(len(item.get("provisions") or ()) for item in items + documents)
    document_quotes = sum(len(item.get("document_quotes") or ()) for item in documents)
    print(
        f"{len(items) + len(documents)} items, {quotes} regulation quotes,"
        f" {document_quotes} document quotes, {len(problems)} problems"
    )
    if problems:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
