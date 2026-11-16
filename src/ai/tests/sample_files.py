"""Small uploaded files built in code, for the parser and ingestion tests."""

import io

import docx


def make_pdf(pages: list[list[str]]) -> bytes:
    """A PDF with one page per item, each with its lines in Helvetica; an empty list makes a page
    with no text layer, like a scan. ASCII text without parentheses or backslashes only."""
    count = len(pages)
    # Objects: 1 catalog, 2 page tree, 3 font, then a page and its content stream per page.
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids ["
        + b" ".join(f"{4 + 2 * i} 0 R".encode() for i in range(count))
        + f"] /Count {count} >>".encode(),
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    ]
    for i, lines in enumerate(pages):
        shown = b" T* ".join(f"({line}) Tj".encode() for line in lines)
        stream = b"BT /F1 10 Tf 12 TL 72 750 Td " + shown + b" ET" if lines else b""
        objects.append(
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792]"
            b" /Resources << /Font << /F1 3 0 R >> >> /Contents " + f"{5 + 2 * i} 0 R >>".encode()
        )
        objects.append(f"<< /Length {len(stream)} >>\nstream\n".encode() + stream + b"\nendstream")
    out = bytearray(b"%PDF-1.4\n")
    offsets = []
    for number, body in enumerate(objects, 1):
        offsets.append(len(out))
        out += f"{number} 0 obj\n".encode() + body + b"\nendobj\n"
    xref = len(out)
    out += f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode()
    out += b"".join(f"{offset:010} 00000 n \n".encode() for offset in offsets)
    out += f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\n".encode()
    out += f"startxref\n{xref}\n%%EOF\n".encode()
    return bytes(out)


def make_docx(blocks: list[tuple[str, object]]) -> bytes:
    """A DOCX from ("h1".."h9", text), ("p", text) and ("table", rows) blocks, in order."""
    document = docx.Document()
    for kind, value in blocks:
        if kind == "p":
            document.add_paragraph(value)
        elif kind == "table":
            table = document.add_table(rows=0, cols=len(value[0]))
            for row in value:
                for cell, text in zip(table.add_row().cells, row, strict=True):
                    cell.text = text
        else:
            document.add_heading(value, level=int(kind[1:]))
    buffer = io.BytesIO()
    document.save(buffer)
    return buffer.getvalue()


LINE = "Keep cold food at 5 C or below and check the fridge twice a day."
