# /// script
# requires-python = ">=3.12"
# dependencies = ["httpx"]
# ///
"""Download the regulations listed in corpus/manifest.json and check their SHA-256.

    uv run corpus/scripts/fetch_corpus.py                    # fetch missing files, verify every hash
    uv run corpus/scripts/fetch_corpus.py --verify           # hash check only, no network
    uv run corpus/scripts/fetch_corpus.py --update-manifest  # record sha256, size and retrieved_at

Files land in corpus/<jurisdiction>/<code>/<version>/<file>. A file whose hash already
matches the manifest is skipped, so running the script twice changes nothing.
"""

import argparse
import hashlib
import json
import sys
from datetime import date
from pathlib import Path

import httpx

CORPUS = Path(__file__).resolve().parent.parent
MANIFEST = CORPUS / "manifest.json"
USER_AGENT = "regulation-corpus-fetch/0.1 (python-httpx)"


def local_path(source: dict) -> Path:
    return CORPUS / source["jurisdiction"].lower() / source["code"] / source["version"] / source["file"]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--verify", action="store_true", help="check local files against the manifest, no network")
    mode.add_argument("--update-manifest", action="store_true", help="record sha256, size and retrieved_at")
    args = parser.parse_args()

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    failures = []
    with httpx.Client(headers={"User-Agent": USER_AGENT}, follow_redirects=True, timeout=120) as client:
        for source in manifest["sources"]:
            path = local_path(source)
            rel = path.relative_to(CORPUS).as_posix()
            if path.exists() and hashlib.sha256(path.read_bytes()).hexdigest() == source["sha256"]:
                print(f"unchanged   {rel}")
                continue
            if args.verify:
                failures.append(f"{rel}: {'hash mismatch' if path.exists() else 'missing'}")
                continue

            try:
                response = client.get(source["download_url"])
                response.raise_for_status()
            except httpx.HTTPError as error:
                failures.append(f"{rel}: download failed: {error}")
                continue
            data = response.content
            if path.suffix == ".pdf" and not data.startswith(b"%PDF"):
                failures.append(f"{rel}: expected a PDF, got {response.headers.get('content-type')}")
                continue

            digest = hashlib.sha256(data).hexdigest()
            if args.update_manifest:
                source.update(sha256=digest, size_bytes=len(data), retrieved_at=date.today().isoformat())
            elif digest != source["sha256"]:
                failures.append(
                    f"{rel}: SHA-256 mismatch: manifest {source['sha256']}, downloaded {digest}. "
                    "The source changed upstream (or no hash is recorded yet): review it, then rerun with --update-manifest."
                )
                continue
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            print(f"downloaded  {rel} ({len(data):,} bytes)")

    if args.update_manifest:
        MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"updated     {MANIFEST.relative_to(CORPUS).as_posix()}")
    for failure in failures:
        print(f"FAILED      {failure}", file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
