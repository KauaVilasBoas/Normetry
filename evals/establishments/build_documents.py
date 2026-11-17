# /// script
# requires-python = ">=3.12"
# dependencies = ["fpdf2>=2.9.0", "python-docx>=1.2.0"]
# [tool.uv]
# # Same package versions on every run, so a rebuild stays byte for byte.
# exclude-newer = "2026-11-17T00:00:00Z"
# ///
"""Render the synthetic establishments' text sources to PDF or DOCX.

    uv run evals/establishments/build_documents.py                  # every */sources/*.md
    uv run evals/establishments/build_documents.py --root <folder>  # another tree, same layout

Each <establishment>/sources/<name>.md becomes <establishment>/documents/<name>.pdf or .docx.
A source is UTF-8 and starts with a front matter block:

    ---
    format: pdf
    title: Manual de Boas Práticas
    ---

`format` is `pdf` or `docx`; `title` goes into the file's metadata only, so the visible title is
the body's first heading. The body, block by block (blank lines end paragraphs and tables):

- `#`, `##`, `###` lines: headings (DOCX: the `Heading 1` to `Heading 3` styles, which the AI
  service turns into section paths);
- `- ` lines: one bullet each;
- `| a | b |` lines: table rows, the first one the header; a `|---|---|` row is skipped;
- any other lines: a paragraph, consecutive lines joined. No inline markup.

PDF text uses the built-in Helvetica, so a PDF source must be Windows-1252 (Portuguese accents,
º, °, € are fine). Output is deterministic: fixed dates and metadata, uncompressed PDF streams
and DOCX zip entries (zlib builds differ between machines), so a rebuild is byte-identical.
"""

import argparse
import io
import re
import sys
import unicodedata
import zipfile
from datetime import UTC, datetime
from pathlib import Path

import docx
from fpdf import FPDF, XPos, YPos

ROOT = Path(__file__).resolve().parent
FIXED_DATE = datetime(2026, 1, 1, tzinfo=UTC)
PRODUCER = "Normetry build_documents.py"
HEADING = re.compile(r"(#+)\s+(.+)")
SEPARATOR_CELL = re.compile(r":?-+:?")
HEADING_SIZES = {1: 16, 2: 13, 3: 11}
BODY_SIZE, LINE = 10, 5

# ("heading", (level, text)), ("paragraph", text), ("bullet", text) or ("table", rows)
type Block = tuple[str, object]


class SourceError(ValueError):
    pass


def read_source(path: Path) -> tuple[str, str, list[Block]]:
    """(format, title, blocks) of one source, or SourceError naming the file and line."""
    lines = unicodedata.normalize("NFC", path.read_text(encoding="utf-8-sig")).splitlines()
    if not lines or lines[0].strip() != "---":
        raise SourceError(f"{path}: must start with a '---' front matter line")
    end = next((i for i, line in enumerate(lines[1:], 1) if line.strip() == "---"), None)
    if end is None:
        raise SourceError(f"{path}: the front matter has no closing '---' line")
    meta: dict[str, str] = {}
    for number, line in enumerate(lines[1:end], 2):
        key, colon, value = line.partition(":")
        if line.strip() and (not colon or key.strip() not in ("format", "title")):
            raise SourceError(f"{path}:{number}: expected 'format: ...' or 'title: ...'")
        if colon:
            meta[key.strip()] = value.strip()
    if meta.get("format") not in ("pdf", "docx"):
        raise SourceError(f"{path}: format must be pdf or docx, got {meta.get('format')!r}")
    if not meta.get("title"):
        raise SourceError(f"{path}: the front matter needs a title")
    if meta["format"] == "pdf":
        for number, line in enumerate(lines, 1):
            try:
                line.encode("cp1252")
            except UnicodeEncodeError as error:
                char = line[error.start]
                raise SourceError(
                    f"{path}:{number}: {char!r} (U+{ord(char):04X}) is not in Windows-1252, "
                    "which the PDF core fonts use"
                ) from None
    blocks = parse_body(path, lines[end + 1 :], end + 2)
    if not blocks:
        raise SourceError(f"{path}: the body is empty")
    return meta["format"], meta["title"], blocks


def parse_body(path: Path, lines: list[str], first_number: int) -> list[Block]:
    blocks: list[Block] = []
    paragraph: list[str] = []
    rows: list[list[str]] = []

    def close() -> None:  # at most one of the two is open
        if paragraph:
            blocks.append(("paragraph", " ".join(paragraph)))
            paragraph.clear()
        if rows:
            blocks.append(("table", rows.copy()))
            rows.clear()

    for number, raw in enumerate(lines, first_number):
        line = raw.strip()
        heading = HEADING.fullmatch(line)
        if line.startswith("|"):
            if paragraph:
                close()
            cells = [cell.strip() for cell in line.strip("|").split("|")]
            if all(SEPARATOR_CELL.fullmatch(cell) for cell in cells):
                continue
            if rows and len(cells) != len(rows[0]):
                raise SourceError(
                    f"{path}:{number}: row has {len(cells)} cells, the table's first row "
                    f"{len(rows[0])}"
                )
            rows.append(cells)
        elif not line:
            close()
        elif heading:
            close()
            level = len(heading[1])
            if level > 3:
                raise SourceError(f"{path}:{number}: only #, ## and ### headings are supported")
            blocks.append(("heading", (level, heading[2].strip())))
        elif line.startswith("- "):
            close()
            blocks.append(("bullet", line[2:].strip()))
        else:
            if rows:
                close()
            paragraph.append(line)
    close()
    return blocks


def render_pdf(title: str, blocks: list[Block]) -> bytes:
    pdf = FPDF(format="A4")
    pdf.core_fonts_encoding = "windows-1252"
    pdf.set_compression(False)
    pdf.set_creation_date(FIXED_DATE)
    pdf.set_producer(PRODUCER)
    pdf.set_creator(PRODUCER)
    pdf.set_title(title)
    pdf.set_margins(20, 20, 20)
    pdf.set_auto_page_break(True, margin=20)
    pdf.add_page()
    for kind, value in blocks:
        if kind == "heading":
            level, text = value
            size = HEADING_SIZES[level]
            if pdf.will_page_break(size * 0.5 + 3 * LINE):  # keep a heading with what follows
                pdf.add_page()
            elif pdf.get_y() > pdf.t_margin:
                pdf.ln(3)
            pdf.set_font("Helvetica", "B", size)
            pdf.multi_cell(0, size * 0.5, text, new_x=XPos.LMARGIN, new_y=YPos.NEXT)
            pdf.ln(2)
        elif kind == "paragraph":
            pdf.set_font("Helvetica", "", BODY_SIZE)
            pdf.multi_cell(0, LINE, value, new_x=XPos.LMARGIN, new_y=YPos.NEXT)
            pdf.ln(2)
        elif kind == "bullet":
            pdf.set_font("Helvetica", "", BODY_SIZE)
            pdf.set_x(pdf.l_margin + 3)
            pdf.cell(5, LINE, "•")
            pdf.multi_cell(0, LINE, value, new_x=XPos.LMARGIN, new_y=YPos.NEXT)
            pdf.ln(1)
        else:
            pdf.set_font("Helvetica", "", BODY_SIZE - 1)
            with pdf.table(line_height=LINE, text_align="LEFT") as table:
                for row in value:
                    cells = table.row()
                    for text in row:
                        cells.cell(text)
            pdf.ln(3)
    return bytes(pdf.output())


def render_docx(title: str, blocks: list[Block]) -> bytes:
    document = docx.Document()
    properties = document.core_properties
    properties.title = title
    properties.author = properties.last_modified_by = PRODUCER
    properties.comments = ""
    properties.created = properties.modified = FIXED_DATE
    properties.revision = 1
    for kind, value in blocks:
        if kind == "heading":
            level, text = value
            document.add_heading(text, level=level)
        elif kind == "paragraph":
            document.add_paragraph(value)
        elif kind == "bullet":
            document.add_paragraph(value, style="List Bullet")
        else:
            table = document.add_table(rows=len(value), cols=len(value[0]))
            table.style = "Table Grid"
            for index, (row, cells) in enumerate(zip(table.rows, value, strict=True)):
                for cell, text in zip(row.cells, cells, strict=True):
                    cell.paragraphs[0].add_run(text).bold = index == 0
    buffer = io.BytesIO()
    document.save(buffer)
    return stable_zip(buffer.getvalue())


def stable_zip(data: bytes) -> bytes:
    """The same package with fixed entry dates and attributes, entries in python-docx's order."""
    out = io.BytesIO()
    with (
        zipfile.ZipFile(io.BytesIO(data)) as source,
        zipfile.ZipFile(out, "w") as target,
    ):
        for name in source.namelist():
            entry = zipfile.ZipInfo(name, date_time=FIXED_DATE.timetuple()[:6])
            entry.create_system = 0  # otherwise 3 on Linux and macOS
            target.writestr(entry, source.read(name), compress_type=zipfile.ZIP_STORED)
    return out.getvalue()


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument("--root", type=Path, default=ROOT, help="folder of establishment folders")
    root = parser.parse_args().root.resolve()
    if not root.is_dir():
        parser.error(f"{root} is not a folder")
    sources = sorted(root.glob("*/sources/*.md"))
    if not sources:
        print(f"no sources under {root}", file=sys.stderr)
    failures = []
    for source in sources:
        try:
            format_, title, blocks = read_source(source)
        except SourceError as error:
            failures.append(str(error))
            continue
        render = render_pdf if format_ == "pdf" else render_docx
        data = render(title, blocks)
        target = source.parent.parent / "documents" / f"{source.stem}.{format_}"
        target.parent.mkdir(exist_ok=True)
        target.write_bytes(data)
        print(f"wrote   {target.relative_to(root).as_posix()} ({len(data):,} bytes)")
    for failure in failures:
        print(f"FAILED  {failure}", file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
