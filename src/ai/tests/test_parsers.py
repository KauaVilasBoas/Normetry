"""Parsing tenant uploads into sections (no database, no model)."""

import csv
import io
import zipfile

import pytest
from sample_files import LINE, make_docx, make_pdf

from normetry_ai.ingestion.chunking import CHUNK_LIMIT, chunk_sections
from normetry_ai.ingestion.parsers import (
    CSV,
    DOCX,
    MAX_BYTES,
    MAX_PAGES,
    PDF,
    TXT,
    Part,
    Rejected,
    parse_file,
)


def rejected(data: bytes, content_type: str) -> tuple[str, str]:
    with pytest.raises(Rejected) as error:
        parse_file(data, content_type)
    return error.value.reason, error.value.text


def test_a_pdf_is_one_section_per_page_with_its_page_count() -> None:
    parsed = parse_file(make_pdf([[LINE, LINE], [], [LINE, "Page three."]]), PDF)

    assert parsed.page_count == 3
    # The empty page has no section; the others keep their number.
    assert parsed.parts == [
        Part("Page 1", f"{LINE} {LINE}"),
        Part("Page 3", f"{LINE} Page three."),
    ]


def test_a_pdf_without_a_text_layer_fails() -> None:
    assert rejected(make_pdf([[], [], []]), PDF)[0] == "no_text_layer"
    # Under 50 characters per page on average: a scan with a footer line.
    assert rejected(make_pdf([["Scanned with an app"], [], []]), PDF)[0] == "no_text_layer"
    # 51 characters a line: three lines over three pages is just enough.
    assert parse_file(make_pdf([[LINE] * 3, [], []]), PDF).page_count == 3


def test_a_pdf_over_the_page_limit_fails_before_its_text_is_read() -> None:
    assert MAX_PAGES == 100
    assert parse_file(make_pdf([[LINE]] * MAX_PAGES), PDF).page_count == MAX_PAGES
    reason, text = rejected(make_pdf([[LINE]] * (MAX_PAGES + 1)), PDF)
    assert (reason, text) == ("too_many_pages", "The PDF has 101 pages; the limit is 100.")


def test_docx_headings_become_the_section_path() -> None:
    data = make_docx(
        [
            ("p", "Intro before any heading."),
            ("h1", "Cold holding"),
            ("p", "Keep it cold."),
            ("h2", "Fridges"),
            ("p", "Check twice a day."),
            ("table", [["Fridge", "Max C"], ["F1", "5"]]),
            ("h2", "Freezers"),
            ("p", "Keep at -18 C."),
            ("h1", "Hot holding"),
            ("h3", "Bain-marie"),  # a skipped level still nests under the open heading
            ("p", "Keep at 60 C or above."),
        ]
    )

    parsed = parse_file(data, DOCX)

    assert parsed.page_count is None
    assert parsed.parts == [
        Part("", "Intro before any heading."),
        Part("Cold holding", "Keep it cold."),
        Part("Cold holding > Fridges", "Check twice a day. Fridge | Max C F1 | 5"),
        Part("Cold holding > Freezers", "Keep at -18 C."),
        Part("Hot holding > Bain-marie", "Keep at 60 C or above."),
    ]


def test_a_docx_that_unpacks_too_large_fails() -> None:
    buffer = io.BytesIO(make_docx([("p", LINE)]))
    with zipfile.ZipFile(buffer, "a", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("word/media/bomb.bin", bytes(51 * 1024 * 1024))

    assert rejected(buffer.getvalue(), DOCX)[0] == "too_large"


@pytest.mark.parametrize(
    ("data", "expected"),
    [
        ("Temperatura mínima: 5 °C.".encode(), "Temperatura mínima: 5 °C."),
        ("﻿With a BOM.".encode(), "With a BOM."),
        ("Temperatura mínima: 5 °C.".encode("latin-1"), "Temperatura mínima: 5 °C."),
        (b"Line one.\r\n\r\nLine\x00two.", "Line one. Line two."),
    ],
    ids=["utf-8", "utf-8-bom", "latin-1", "nul-and-breaks"],
)
def test_text_is_utf_8_else_latin_1(data: bytes, expected: str) -> None:
    assert parse_file(data, TXT).parts == [Part("", expected)]


def test_csv_rows_are_grouped_with_the_header_repeated() -> None:
    rows = [["Date", "Fridge", "Temperature C"]] + [
        [f"2026-11-{day:02}", "F1", f"{day % 7}.5"] for day in range(1, 201)
    ]
    buffer = io.StringIO()
    csv.writer(buffer, delimiter=";").writerows(rows)

    parsed = parse_file(buffer.getvalue().encode(), "text/csv; charset=utf-8")

    assert len(parsed.parts) > 1
    assert all(part.text.startswith("Date | Fridge | Temperature C\n") for part in parsed.parts)
    assert all(len(part.text) <= CHUNK_LIMIT for part in parsed.parts)
    # Every data row lands in exactly one group, in order, and the paths count them.
    lines = [line for part in parsed.parts for line in part.text.split("\n")[1:]]
    assert lines == [" | ".join(row) for row in rows[1:]]
    assert parsed.parts[0].section_path.startswith("Rows 1-")
    assert parsed.parts[-1].section_path.endswith("-200")
    # Each group is one chunk.
    assert len(chunk_sections(parsed.parts)) == len(parsed.parts)


@pytest.mark.parametrize(
    ("data", "content_type", "reason"),
    [
        (b"%PDF-1.4 not really", PDF, "parse_error"),
        (b"not a zip", DOCX, "parse_error"),
        (b"   \n\t ", TXT, "parse_error"),
        (b"Date,Fridge\n", CSV, "parse_error"),  # a header and no rows
        (b"<html></html>", "text/html", "unsupported_format"),
        (b"x" * (MAX_BYTES + 1), TXT, "too_large"),
    ],
    ids=["corrupt-pdf", "not-a-docx", "blank-text", "header-only-csv", "html", "over-10-mib"],
)
def test_files_that_cannot_be_indexed_fail_with_their_reason(
    data: bytes, content_type: str, reason: str
) -> None:
    assert rejected(data, content_type)[0] == reason


def test_media_type_parameters_and_case_are_ignored() -> None:
    assert parse_file(b"Hello.", "Text/Plain; charset=utf-8").parts == [Part("", "Hello.")]
