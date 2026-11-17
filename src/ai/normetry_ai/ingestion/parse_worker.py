"""Parse one untrusted upload in a child process (`sandbox.py` starts it).

`python -I -m normetry_ai.ingestion.parse_worker <content type>` reads the file's bytes from stdin
and writes one JSON object to stdout: `{"parts": [{"section_path", "text"}, ...], "page_count"}`,
or `{"rejected": {"reason", "text"}}`. It imports only the parsers: nothing that reads settings or
reaches the database, blob storage or the broker.
"""

import contextlib
import json
import sys
from dataclasses import asdict

from normetry_ai.ingestion.parsers import Rejected, parse_file


def main() -> None:
    data = sys.stdin.buffer.read()
    # A library that prints must not corrupt the one JSON object on stdout.
    with contextlib.redirect_stdout(sys.stderr):
        try:
            parsed = parse_file(data, sys.argv[1])
            result: dict = {
                "parts": [asdict(part) for part in parsed.parts],
                "page_count": parsed.page_count,
            }
        except Rejected as error:
            result = {"rejected": {"reason": error.reason, "text": error.text}}
    sys.stdout.write(json.dumps(result))


if __name__ == "__main__":
    main()
