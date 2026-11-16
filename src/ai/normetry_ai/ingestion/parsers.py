"""Parse a tenant's uploaded file into sections: PDF, DOCX, TXT and CSV (phase 2a design).

A file the worker cannot index for a reason the user can act on raises `Rejected` with a reason
code of `DocumentIngestionFailed` (src/shared/contracts/messages). Untrusted input: any other
error a parser library raises on the file becomes `parse_error`.

- PDF: the text layer (PDFium), one section per page ("Page 3"). Over 100 pages fails
  `too_many_pages`; under 50 non-whitespace characters per page on average fails `no_text_layer`.
  A page of text holds 1,500 to 3,000, a title-only cover page a few dozen, a scan none or a
  scanner app's footer, so 50 tells scans from text while letting a short certificate through.
- DOCX: paragraphs and tables in document order; "Heading N" paragraphs build the section path
  ("Scope > Cold holding"). The unpacked size is checked first (a zip bomb fails `too_large`).
- TXT: UTF-8 (a BOM is dropped), else Latin-1, which decodes any bytes. One section.
- CSV: the same decoding, delimiter sniffed. Rows are grouped into sections of at most
  CHUNK_LIMIT characters, each starting with the header row, so every chunk of a temperature log
  says what its columns are ("Rows 1-14").
"""

import csv
import io
import zipfile
from collections.abc import Callable
from dataclasses import dataclass

import docx
import pypdfium2 as pdfium
from docx.table import Table
from docx.text.paragraph import Paragraph

from normetry_ai.ingestion.chunking import CHUNK_LIMIT
from normetry_ai.ingestion.normalize import normalize_text
from normetry_ai.messaging.contracts import FailureReason

MAX_BYTES = 10 * 1024 * 1024
MAX_PAGES = 100
MIN_TEXT_PER_PAGE = 50
MAX_DOCX_UNPACKED_BYTES = 50 * 1024 * 1024

PDF = "application/pdf"
DOCX = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
TXT = "text/plain"
CSV = "text/csv"


class Rejected(Exception):
    """A file that cannot be indexed: a permanent failure, reported to the core."""

    def __init__(self, reason: FailureReason, text: str) -> None:
        super().__init__(text)
        self.reason = reason
        self.text = text


@dataclass(frozen=True)
class Part:
    """One section of a document; an empty path means the document has no heading there."""

    section_path: str
    text: str


@dataclass(frozen=True)
class Parsed:
    parts: list[Part]
    page_count: int | None  # PDF only


def _clean(text: str) -> str:
    # PostgreSQL text cannot hold NUL.
    return normalize_text(text.replace("\x00", " "))


def _decode(data: bytes) -> str:
    try:
        return data.decode("utf-8-sig")
    except UnicodeDecodeError:
        return data.decode("latin-1")


def _pdf(data: bytes) -> Parsed:
    pdf = pdfium.PdfDocument(data)
    try:
        pages = len(pdf)
        if pages > MAX_PAGES:
            raise Rejected(
                "too_many_pages", f"The PDF has {pages} pages; the limit is {MAX_PAGES}."
            )
        texts = [_clean(pdf[index].get_textpage().get_text_range()) for index in range(pages)]
    finally:
        pdf.close()
    if sum(len(text.replace(" ", "")) for text in texts) < MIN_TEXT_PER_PAGE * max(pages, 1):
        raise Rejected(
            "no_text_layer", "The PDF has no text layer. Scanned pages cannot be indexed."
        )
    parts = [Part(f"Page {number}", text) for number, text in enumerate(texts, 1) if text]
    return Parsed(parts, pages)


def _heading_level(paragraph: Paragraph) -> int | None:
    name = paragraph.style.name if paragraph.style is not None else None
    if name and name.startswith("Heading ") and name[8:].isdigit():
        return max(int(name[8:]), 1)
    return None


def _docx(data: bytes) -> Parsed:
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        if sum(info.file_size for info in archive.infolist()) > MAX_DOCX_UNPACKED_BYTES:
            raise Rejected("too_large", "The DOCX file unpacks to more than 50 MiB.")
    headings: list[str] = []
    body: list[str] = []
    parts: list[Part] = []

    def close_section() -> None:
        text = _clean(" ".join(body))
        if text:
            parts.append(Part(" > ".join(headings), text))
        body.clear()

    for block in docx.Document(io.BytesIO(data)).iter_inner_content():
        if isinstance(block, Table):
            body.extend(" | ".join(cell.text for cell in row.cells) for row in block.rows)
            continue
        level = _heading_level(block)
        heading = _clean(block.text) if level is not None else ""
        if not heading:
            body.append(block.text)
            continue
        close_section()
        del headings[level - 1 :]
        headings.append(heading)
    close_section()
    return Parsed(parts, None)


def _txt(data: bytes) -> Parsed:
    text = _clean(_decode(data))
    return Parsed([Part("", text)] if text else [], None)


def _csv(data: bytes) -> Parsed:
    text = _decode(data)
    try:
        dialect: type[csv.Dialect] | csv.Dialect = csv.Sniffer().sniff(text[:4096], ",;\t|")
    except csv.Error:
        dialect = csv.excel
    rows = [[_clean(cell) for cell in row] for row in csv.reader(io.StringIO(text), dialect)]
    header, *records = [" | ".join(row) for row in rows if any(row)] or [""]
    parts: list[Part] = []
    group: list[str] = []
    first = 1

    def close_group(last: int) -> None:
        path = f"Rows {first}-{last}" if last > first else f"Row {first}"
        parts.append(Part(path, "\n".join([header, *group])))

    for number, record in enumerate(records, 1):
        # ponytail: a single row longer than the limit makes an overlong group, which the chunker
        # then cuts at sentence ends without repeating the header.
        if group and len(header) + sum(len(line) + 1 for line in [*group, record]) > CHUNK_LIMIT:
            close_group(number - 1)
            group, first = [], number
        group.append(record)
    if group:
        close_group(len(records))
    return Parsed(parts, None)


_PARSERS: dict[str, tuple[str, Callable[[bytes], Parsed]]] = {
    PDF: ("PDF", _pdf),
    DOCX: ("DOCX", _docx),
    TXT: ("text", _txt),
    CSV: ("CSV", _csv),
}


def parse_file(data: bytes, content_type: str) -> Parsed:
    """The sections of a file of one of the four accepted media types (parameters ignored)."""
    format_ = _PARSERS.get(content_type.split(";")[0].strip().lower())
    if format_ is None:
        raise Rejected("unsupported_format", "Only PDF, DOCX, TXT and CSV files can be indexed.")
    if len(data) > MAX_BYTES:
        raise Rejected("too_large", "The file is larger than 10 MiB.")
    name, parser = format_
    try:
        parsed = parser(data)
    except Rejected:
        raise
    except Exception as error:  # the libraries raise many types on malformed files
        raise Rejected("parse_error", f"The file could not be read as {name}.") from error
    if not parsed.parts:
        raise Rejected("parse_error", "The file holds no text to index.")
    return parsed
