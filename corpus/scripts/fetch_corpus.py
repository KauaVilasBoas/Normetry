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

An HTML page is stored without its cache-busting `?YYYYMMDD` link suffixes: AnvisaLegis stamps
the current date on every stylesheet, script and image link, so the raw page changes daily
while the text does not. Local files are hashed the same way, so a page fetched before the
suffixes were stripped still matches; it is rewritten without them (with --verify too).
src/ai/normetry_ai/ingestion/corpus.py strips them with the same pattern before its own check.
"""

import argparse
import hashlib
import json
import re
import sys
from datetime import date
from pathlib import Path

import httpx

CORPUS = Path(__file__).resolve().parent.parent
MANIFEST = CORPUS / "manifest.json"
USER_AGENT = "regulation-corpus-fetch/0.1 (python-httpx)"
# A date query string closing an attribute value: `main.css?20261120"`.
CACHE_BUSTER = re.compile(rb"\?\d{8}(?=[\"'])")


def local_path(source: dict) -> Path:
    return CORPUS / source["jurisdiction"].lower() / source["code"] / source["version"] / source["file"]


def without_cache_busters(path: Path, data: bytes) -> bytes:
    """The page as pinned: an HTML file loses its daily `?YYYYMMDD` link suffixes."""
    return CACHE_BUSTER.sub(b"", data) if path.suffix == ".html" else data


def local_copy_is_pinned(path: Path, sha256: str) -> bool:
    """Whether the local file, stripped of cache busters, has the manifest's hash. A copy that
    still carries them is rewritten stripped, so every reader of the file sees the pinned bytes."""
    if not path.exists():
        return False
    data = path.read_bytes()
    pinned = without_cache_busters(path, data)
    if hashlib.sha256(pinned).hexdigest() != sha256:
        return False
    if pinned != data:
        path.write_bytes(pinned)
    return True


def _fetch_source(client: httpx.Client, source: dict, path: Path, update: bool) -> str | None:
    """Download one source to `path`, or return why it failed. With `update`, the download's
    sha256, size and date go into the manifest entry instead of being checked against it."""
    rel = path.relative_to(CORPUS).as_posix()
    try:
        response = client.get(source["download_url"])
        response.raise_for_status()
    except httpx.HTTPError as error:
        return f"{rel}: download failed: {error}"
    data = without_cache_busters(path, response.content)
    if path.suffix == ".pdf" and not data.startswith(b"%PDF"):
        return f"{rel}: expected a PDF, got {response.headers.get('content-type')}"

    digest = hashlib.sha256(data).hexdigest()
    if update:
        source.update(sha256=digest, size_bytes=len(data), retrieved_at=date.today().isoformat())
    elif digest != source["sha256"]:
        return (
            f"{rel}: SHA-256 mismatch: manifest {source['sha256']}, downloaded {digest}. "
            "The source changed upstream (or no hash is recorded yet): review it, then rerun with --update-manifest."
        )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    print(f"downloaded  {rel} ({len(data):,} bytes)")
    return None


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
            if local_copy_is_pinned(path, source["sha256"]):
                print(f"unchanged   {rel}")
                continue
            if args.verify:
                failures.append(f"{rel}: {'hash mismatch' if path.exists() else 'missing'}")
                continue
            if failure := _fetch_source(client, source, path, args.update_manifest):
                failures.append(failure)

    if args.update_manifest:
        MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"updated     {MANIFEST.relative_to(CORPUS).as_posix()}")
    for failure in failures:
        print(f"FAILED      {failure}", file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
