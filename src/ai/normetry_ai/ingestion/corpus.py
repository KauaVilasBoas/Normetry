"""Parse the regulation files listed in corpus/manifest.json into citable sections.

    uv run python -m normetry_ai.ingestion.corpus    # writes src/ai/.data/corpus-sections.jsonl

Only the parts the phase 1 design makes citable are kept:

- FDA Food Code 2026: chapters 1 to 8, one section per code section ("3-501.16"). No annexes.
- Regulation (EC) No 852/2004, en and pt: the articles ("Article 4") and the annexes, cut by part
  or chapter ("Annex I, Part A", "Annex II, Chapter I"). The consolidated text has no recitals.
  Paths are the same in both languages, so the two versions of a provision line up.
- RDC 216/2004: the Technical Regulation in the annex, one section per item ("4.8.15"). The
  resolution's preamble articles stay out.

The files must match the manifest's SHA-256 (fetch them with corpus/scripts/fetch_corpus.py).
"""

import argparse
import hashlib
import json
import re
from collections import Counter
from collections.abc import Callable, Iterable
from dataclasses import asdict, dataclass
from html.parser import HTMLParser
from pathlib import Path

import pypdfium2 as pdfium

from normetry_ai.ingestion.normalize import normalize_text

CORPUS_DIR = Path(__file__).resolve().parents[4] / "corpus"
DEFAULT_OUTPUT = Path(__file__).resolve().parents[2] / ".data" / "corpus-sections.jsonl"


@dataclass(frozen=True)
class Section:
    """One citable unit of a regulation; `section_path` locates it inside its source."""

    source_id: str
    jurisdiction: str
    language: str
    version: str
    section_path: str
    text: str


# A heading test returns (section_path, rest of the line) for a section heading, _CLOSE for a
# structural line (chapter or part title) that ends the open section, or None for content.
_CLOSE = ("", "")
Heading = Callable[[str], tuple[str, str] | None]


def _group(lines: Iterable[str], heading: Heading) -> list[tuple[str, str]]:
    """Cut lines into (section_path, raw text). Lines outside any section are dropped."""
    sections: list[tuple[str, list[str]]] = []
    body: list[str] | None = None
    for line in lines:
        hit = heading(line)
        if hit is None:
            if body is not None:
                body.append(line)
        elif hit is _CLOSE:
            body = None
        else:
            body = [hit[1]]
            sections.append((hit[0], body))
    return [(path, "\n".join(body)) for path, body in sections]


def _pdf_pages(data: bytes) -> list[list[str]]:
    """Text layer of each page as stripped, non-empty lines (the PDFs need no OCR)."""
    pdf = pdfium.PdfDocument(data)
    try:
        pages = [pdf[index].get_textpage().get_text_range() for index in range(len(pdf))]
    finally:
        pdf.close()
    return [[line.strip() for line in page.splitlines() if line.strip()] for page in pages]


# --- FDA Food Code -----------------------------------------------------------------------------

# Every page starts with a running header naming its part, then the page number.
_FDA_CHAPTER_PAGE = re.compile(r"FDA Food Code 2026 Chapter [1-8]\b")
# A title starts with a capital or a quote ('4-202.13 "V" Threads, ...'); a wrapped cross-reference
# ("2-201.12 and 2-201.13.") does not.
# Part 4-10 has four-digit subparts: "4-1001.11".
_FDA_SECTION = re.compile(r"(\d-\d{3,4}\.\d{2})\s+([A-Z\"“].*)")
# Chapter title, part ("3-5 Limitation of ...") and subpart ("3-501 Temperature ...") headings.
_FDA_STRUCTURE = re.compile(r"Chapter \d\.|Parts:|Subparts:|\d-\d{1,4}\s+[A-Z]")


def _fda_heading(line: str) -> tuple[str, str] | None:
    if match := _FDA_SECTION.fullmatch(line):
        return match[1], match[2]
    return _CLOSE if _FDA_STRUCTURE.match(line) else None


def _fda_sections(data: bytes, _language: str) -> list[tuple[str, str]]:
    # The Food Code is set without hyphenation: a hyphen PDFium marks at a line end (U+FFFE) is
    # part of the term ("self-service", "*READY-TO-EAT"), so it stays a hyphen.
    lines = [
        line.replace("￾", "-")
        for page in _pdf_pages(data)
        if page and _FDA_CHAPTER_PAGE.match(page[0])
        for line in page[2:]  # past the running header and the page number
    ]
    return _group(lines, _fda_heading)


# --- Regulation (EC) No 852/2004 (EUR-Lex consolidated PDF) -----------------------------------

_EU_PAGE_HEADER = re.compile(r"\d{5}R\d{4} — [A-Z]{2} — ")
# Consolidation markers: "►M2 ... ◄" around amended text, "▼C1" lines before each layer.
_EU_MARKER = re.compile(r"[►▼][A-Z]\d*|◄")
_EU_FOOTNOTE = re.compile(r"\(\d+\) ")
_EU_HEADINGS = {
    "en": re.compile(
        r"Article (?P<article>\d+)|ANNEX (?P<annex>[IVX]+)"
        r"|CHAPTER (?P<chapter>[IVX]+a?)|PART (?P<part>[A-Z]): (?P<rest>.*)"
    ),
    "pt": re.compile(
        r"Artigo (?P<article>\d+)\.o|ANEXO (?P<annex>[IVX]+)"
        r"|CAPÍTULO (?P<chapter>[IVX]+(?:-A)?)|PARTE (?P<part>[A-Z]): (?P<rest>.*)"
    ),
}


def _eu_lines(data: bytes) -> list[str]:
    lines = []
    for page in _pdf_pages(data):
        page = [line for line in page if not _EU_PAGE_HEADER.match(line)]
        # Footnotes close the page: a marker line, then "(1) OJ L ...". A "(1)" paragraph in the
        # body (Annex II, Chapter Va) has no marker line before it.
        for index in range(1, len(page)):
            if _EU_MARKER.fullmatch(page[index - 1]) and _EU_FOOTNOTE.match(page[index]):
                page = page[:index]
                break
        lines += [clean for line in page if (clean := _EU_MARKER.sub("", line).strip())]
    return lines


def _eu_sections(data: bytes, language: str) -> list[tuple[str, str]]:
    pattern = _EU_HEADINGS[language]
    annex = ""

    def heading(line: str) -> tuple[str, str] | None:
        nonlocal annex
        match = pattern.fullmatch(line)
        if match is None:
            return None
        if match["article"]:
            return f"Article {match['article']}", ""
        if match["annex"]:
            annex = f"Annex {match['annex']}"
            return annex, ""
        if match["chapter"]:
            if not annex:  # a chapter of the enacting terms: only a title over its articles
                return _CLOSE
            return f"{annex}, Chapter {match['chapter'].replace('-A', 'a')}", ""
        return f"{annex}, Part {match['part']}", match["rest"]

    return _group(_eu_lines(data), heading)


# --- Anvisa RDC 216/2004 (AnvisaLegis compiled page) ------------------------------------------


class _Paragraphs(HTMLParser):
    """Text of every <p> element, in document order."""

    def __init__(self) -> None:
        super().__init__()
        self.paragraphs: list[str] = []
        self._current: list[str] | None = None

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag == "p":
            self._current = []

    def handle_endtag(self, tag: str) -> None:
        if tag == "p" and self._current is not None:
            self.paragraphs.append(" ".join("".join(self._current).split()))
            self._current = None

    def handle_data(self, data: str) -> None:
        if self._current is not None:
            self._current.append(data)


# "1 - ALCANCE", "1.1. Objetivo", "2- DEFINIÇÕES", "4.8.15 Após serem submetidos ..."
_RDC_ITEM = re.compile(r"(\d+(?:\.\d+)*)\.?\s*-?\s*([A-ZÀ-Ý].*)")


def _rdc_sections(data: bytes, _language: str) -> list[tuple[str, str]]:
    parser = _Paragraphs()
    # The page declares UTF-8 but is encoded in Latin-1 (Windows-1252).
    parser.feed(data.decode("cp1252"))
    paragraphs = parser.paragraphs
    # The Technical Regulation runs from the "ANEXO" title to the publication note.
    start = paragraphs.index("ANEXO")
    end = next(i for i, p in enumerate(paragraphs) if p.startswith("PUB D.O.U."))
    # "Redações Anteriores" is the site's link to superseded wording, not text of the act.
    regulation = [p for p in paragraphs[start:end] if p != "Redações Anteriores"]

    def heading(paragraph: str) -> tuple[str, str] | None:
        match = _RDC_ITEM.fullmatch(paragraph)
        return (match[1], match[2]) if match else None

    return _group(regulation, heading)


# --- Manifest and command ---------------------------------------------------------------------

_PARSERS: dict[str, Callable[[bytes, str], list[tuple[str, str]]]] = {
    "food-code": _fda_sections,
    "reg-852-2004": _eu_sections,
    "rdc-216-2004": _rdc_sections,
}


def load_manifest(corpus_dir: Path = CORPUS_DIR) -> list[dict]:
    """The sources listed in corpus/manifest.json."""
    return json.loads((corpus_dir / "manifest.json").read_text(encoding="utf-8"))["sources"]


def parse_source(source: dict, corpus_dir: Path = CORPUS_DIR) -> list[Section]:
    """Sections of one manifest source, in document order.

    Raises FileNotFoundError when the file has not been fetched, ValueError when its SHA-256 does
    not match the manifest.
    """
    path = corpus_dir / source["jurisdiction"].lower() / source["code"] / source["version"]
    data = (path / source["file"]).read_bytes()
    if hashlib.sha256(data).hexdigest() != source["sha256"]:
        raise ValueError(
            f"{source['file']} does not match the manifest's SHA-256. "
            "Fetch it again with corpus/scripts/fetch_corpus.py."
        )
    sections = []
    for section_path, raw in _PARSERS[source["code"]](data, source["language"]):
        text = normalize_text(raw)
        # Only an uppercase title ("4.1 EDIFICAÇÃO, ...", "ANNEX I PRIMARY PRODUCTION"): a heading
        # over its subdivisions, with no provision of its own.
        if not text or text.isupper():
            continue
        sections.append(
            Section(
                source_id=source["id"],
                jurisdiction=source["jurisdiction"],
                language=source["language"],
                version=source["version"],
                section_path=section_path,
                text=text,
            )
        )
    return sections


def parse_corpus(corpus_dir: Path = CORPUS_DIR) -> list[Section]:
    """Sections of every source in the manifest, in manifest order."""
    return [
        section
        for source in load_manifest(corpus_dir)
        for section in parse_source(source, corpus_dir)
    ]


def main(argv: list[str] | None = None) -> None:
    """Write every source's sections to a JSONL file for inspection (one section per line)."""
    parser = argparse.ArgumentParser(description=main.__doc__)
    parser.add_argument("--corpus", type=Path, default=CORPUS_DIR, help="the corpus directory")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT, help="the JSONL file")
    args = parser.parse_args(argv)

    sections = parse_corpus(args.corpus)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8", newline="\n") as output:
        for section in sections:
            output.write(json.dumps(asdict(section), ensure_ascii=False) + "\n")
    for source_id, count in Counter(section.source_id for section in sections).items():
        print(f"{count:5}  {source_id}")
    print(f"wrote {args.output}")


if __name__ == "__main__":
    main()
