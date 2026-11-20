"""The synthetic demo establishments (evals/establishments/*): manifests, documents and the
expected findings an inspection is measured against (contract:
specs/001-checklists-synthetic-establishments/contracts/expected-findings.md).

Offline: the committed files through the worker's own parser, no database, no model.
"""

import importlib.util
import json
from collections import Counter
from datetime import date
from functools import cache
from pathlib import Path

import pytest

from normetry_ai.ingestion.normalize import normalize_text
from normetry_ai.ingestion.parsers import CSV, DOCX, PDF, TXT, Rejected, parse_file
from normetry_ai.inspection.checklist import CHECKLIST_CODES, items_for

ESTABLISHMENTS = Path(__file__).resolve().parents[3] / "evals" / "establishments"
FOLDERS = sorted(path.parent for path in ESTABLISHMENTS.glob("*/expected-findings.json"))
# The demo establishments of DemoTenantSeed (src/core) and their jurisdictions.
DEMO = {"Bistro São Paulo": "BR", "Bistro Lisboa": "EU", "Bakery Austin": "US"}
TYPES = {"Sop", "License", "Log", "Report", "Other"}
VERDICTS = {"compliant", "non_compliant", "insufficient_evidence"}
CONTENT_TYPES = {".pdf": PDF, ".docx": DOCX, ".txt": TXT, ".csv": CSV}
DISCLAIMERS = ("Documento fictício para demonstração", "Fictional document for demonstration")

per_establishment = pytest.mark.parametrize("folder", FOLDERS, ids=lambda folder: folder.name)


@cache
def _json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def _manifest(folder: Path) -> dict:
    return _json(folder / "documents" / "manifest.json")


def _findings(folder: Path) -> dict:
    return _json(folder / "expected-findings.json")


def _files(folder: Path) -> set[str]:
    return {entry["file"] for entry in _manifest(folder)["documents"]}


def _near_misses(folder: Path) -> list[dict]:
    """The findings with a near-miss document: one that looks like evidence but misses the item."""
    return [finding for finding in _findings(folder)["findings"] if finding.get("nearMiss")]


def _near_miss_files(folder: Path) -> set[str]:
    return {entry["file"] for finding in _near_misses(folder) for entry in finding["nearMiss"]}


@cache
def _texts(folder: Path) -> dict[str, list[str]]:
    """Each manifest file's parts as the worker parses them, normalized as quotes are."""
    texts = {}
    for file in sorted(_files(folder)):
        path = folder / "documents" / file
        try:
            parsed = parse_file(path.read_bytes(), CONTENT_TYPES.get(path.suffix, ""))
        except Rejected as error:
            pytest.fail(f"{folder.name}/{file}: rejected {error.reason}: {error.text}")
        texts[file] = [normalize_text(part.text) for part in parsed.parts]
    return texts


def _applicable(jurisdiction: str) -> set[str]:
    return {item.id for items in items_for(jurisdiction, "Restaurant").values() for item in items}


def _iso_date(value: object) -> bool:
    try:
        return isinstance(value, str) and date.fromisoformat(value).isoformat() == value
    except ValueError:
        return False


def test_every_demo_establishment_has_a_folder() -> None:
    assert sorted(_findings(folder)["establishment"] for folder in FOLDERS) == sorted(DEMO)


@per_establishment
def test_the_manifest_lists_6_to_10_existing_documents_besides_near_misses(folder: Path) -> None:
    documents = _manifest(folder)["documents"]
    problems = []
    for entry in documents:
        file = entry["file"]
        if not (folder / "documents" / file).is_file():
            problems.append(f"{file}: no such file")
        if entry["type"] not in TYPES:
            problems.append(f"{file}: unknown type {entry['type']!r}")
        if "expiresOn" in entry and not _iso_date(entry["expiresOn"]):
            problems.append(f"{file}: expiresOn {entry['expiresOn']!r} is not YYYY-MM-DD")

    assert problems == []
    # 6 to 10 (the phase 2 design), plus the near-miss documents added on top of them (#116).
    assert 6 <= len(documents) - len(_near_miss_files(folder)) <= 10


@per_establishment
def test_every_document_parses_and_says_it_is_fictional(folder: Path) -> None:
    texts = _texts(folder)

    assert sorted(texts) == sorted(_files(folder))
    assert [
        file
        for file, parts in texts.items()
        if not any(disclaimer in part for part in parts for disclaimer in DISCLAIMERS)
    ] == []


@per_establishment
def test_the_findings_name_the_establishment_its_jurisdiction_and_checklist(folder: Path) -> None:
    findings = _findings(folder)

    assert findings["establishment"] == _manifest(folder)["establishment"]
    assert findings["jurisdiction"] == DEMO[findings["establishment"]]
    assert findings["checklist"] == CHECKLIST_CODES[findings["jurisdiction"]]


@per_establishment
def test_every_applicable_item_has_exactly_one_finding(folder: Path) -> None:
    findings = _findings(folder)
    applicable = _applicable(findings["jurisdiction"])
    ids = Counter(finding["item"] for finding in findings["findings"])

    assert {
        "missing": sorted(applicable - ids.keys()),
        "unknown or not applicable": sorted(ids.keys() - applicable),
        "duplicate": sorted(item for item, count in ids.items() if count > 1),
    } == {"missing": [], "unknown or not applicable": [], "duplicate": []}


@per_establishment
def test_every_quote_is_in_one_part_of_a_manifest_file(folder: Path) -> None:
    files, texts = _files(folder), _texts(folder)
    problems = []
    for finding in _findings(folder)["findings"]:
        item, verdict, evidence = finding["item"], finding["verdict"], finding["evidence"]
        if verdict not in VERDICTS:
            problems.append(f"{item}: unknown verdict {verdict!r}")
        if (verdict == "insufficient_evidence") == bool(evidence):
            problems.append(f"{item}: {verdict} with {len(evidence)} evidence entries")
        for entry in evidence:
            file, quote = entry["file"], normalize_text(entry["quote"])
            if file not in files:
                problems.append(f"{item}: {file} is not in the manifest")
            elif not quote or not any(quote in part for part in texts[file]):
                problems.append(f"{item}: not in any one part of {file}: {entry['quote']!r}")

    assert problems == []


@per_establishment
def test_5_to_8_items_are_non_compliant_and_at_least_2_lack_evidence(folder: Path) -> None:
    verdicts = Counter(finding["verdict"] for finding in _findings(folder)["findings"])

    assert 5 <= verdicts["non_compliant"] <= 8
    assert verdicts["insufficient_evidence"] >= 2


@per_establishment
def test_the_injection_document_targets_1_to_3_applicable_items(folder: Path) -> None:
    findings = _findings(folder)
    injection = findings["injection"]

    assert injection["file"] in _files(folder)
    assert 1 <= len(injection["targets"]) <= 3
    assert set(injection["targets"]) <= _applicable(findings["jurisdiction"])
    assert injection["expected"] == "no_effect"


def test_the_committed_pdf_and_docx_files_are_what_build_documents_renders() -> None:
    # ponytail: fpdf2 is a dependency of the build script only, so this skips in the src/ai venv;
    # add fpdf2 (at the script's pinned version) to the dev group to run it in CI.
    pytest.importorskip("fpdf", reason="fpdf2 is not installed (build_documents.py's dependency)")
    spec = importlib.util.spec_from_file_location(
        "build_documents", ESTABLISHMENTS / "build_documents.py"
    )
    assert spec is not None and spec.loader is not None
    build = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(build)
    sources = sorted(ESTABLISHMENTS.glob("*/sources/*.md"))
    stale = []
    for source in sources:
        format_, title, blocks = build.read_source(source)
        render = build.render_pdf if format_ == "pdf" else build.render_docx
        target = source.parent.parent / "documents" / f"{source.stem}.{format_}"
        if not target.is_file() or render(title, blocks) != target.read_bytes():
            stale.append(target.relative_to(ESTABLISHMENTS).as_posix())

    assert sources
    assert stale == []
