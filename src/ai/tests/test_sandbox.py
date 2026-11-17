"""Parsing in a child process (`ingestion/sandbox.py`): the same result as in-process for good and
rejected files, `parse_error` for a child that crashes, hangs, runs out of memory or writes
anything else, and no credentials in the child's environment. Misbehaving children are stand-in
scripts put in place of the worker module (`sandbox.COMMAND`)."""

import hashlib
import json
import subprocess
import sys
import time
from uuid import uuid4

import pytest
from fakes import FakeContainer, FakeEmbedder
from sample_files import LINE, make_docx, make_pdf
from sqlalchemy import Engine

from normetry_ai.ingestion import sandbox
from normetry_ai.ingestion.documents import BlobStore, DocumentWorker
from normetry_ai.ingestion.parsers import CSV, DOCX, PDF, TXT, Parsed, Rejected, parse_file
from normetry_ai.messaging.contracts import DocumentUploaded
from normetry_ai.settings import ParseSettings

LIMITS = ParseSettings(_env_file=None)


def parse(data: bytes, content_type: str, **limits: float) -> Parsed:
    limits = {
        "timeout_s": LIMITS.parse_timeout_seconds,
        "memory_mb": LIMITS.parse_memory_mb,
        **limits,
    }
    return sandbox.parse_in_subprocess(data, content_type, **limits)


def rejected(data: bytes, content_type: str, **limits: float) -> tuple[str, str]:
    with pytest.raises(Rejected) as error:
        parse(data, content_type, **limits)
    return error.value.reason, error.value.text


def child(monkeypatch: pytest.MonkeyPatch, code: str) -> None:
    """Run `code` as the child instead of the worker module (same environment and limits)."""
    monkeypatch.setattr(sandbox, "COMMAND", [sys.executable, "-I", "-c", code])


def reply(text: str) -> str:
    """Code for a child that reads the file and returns one section holding `text`'s value."""
    return (
        "import json, os, sys; sys.stdin.buffer.read(); sys.stdout.write(json.dumps("
        f"{{'parts': [{{'section_path': '', 'text': {text}}}], 'page_count': None}}))"
    )


@pytest.mark.parametrize(
    ("data", "content_type"),
    [
        (make_pdf([[LINE, LINE], [], [LINE]]), PDF),
        (make_docx([("p", "Intro."), ("h1", "Cold holding"), ("p", LINE)]), DOCX),
        ("Temperatura mínima: 5 °C.".encode("latin-1"), TXT),
        (f"Date;Fridge\n2026-11-16;{LINE}\n".encode(), "text/csv; charset=utf-8"),
    ],
    ids=["pdf", "docx", "txt", "csv"],
)
def test_a_file_parses_the_same_as_in_process(data: bytes, content_type: str) -> None:
    assert parse(data, content_type) == parse_file(data, content_type)


@pytest.mark.parametrize(
    ("data", "content_type"),
    [
        (make_pdf([[], []]), PDF),
        (b"%PDF-1.4 not really", PDF),
        (b"<html></html>", "text/html"),
        (b"Date,Fridge\n", CSV),
    ],
    ids=["no-text-layer", "corrupt-pdf", "html", "header-only-csv"],
)
def test_a_rejected_file_keeps_its_reason_and_text(data: bytes, content_type: str) -> None:
    with pytest.raises(Rejected) as in_process:
        parse_file(data, content_type)

    assert rejected(data, content_type) == (in_process.value.reason, in_process.value.text)


@pytest.mark.parametrize(
    "code",
    [
        "import sys; sys.exit(3)",
        "import os; os.abort()",
        "raise RuntimeError('parser bug')",
        "print('not json')",
        reply("1"),  # a section that is not text
        "import json; print(json.dumps({'rejected': {'reason': 'other', 'text': 'x'}}))",
        "import json; print(json.dumps({'parts': []}))",  # no page count
    ],
    ids=["exit-3", "abort", "exception", "not-json", "bad-part", "unknown-reason", "incomplete"],
)
def test_a_child_that_crashes_or_writes_anything_else_fails_with_parse_error(
    monkeypatch: pytest.MonkeyPatch, code: str
) -> None:
    child(monkeypatch, code)

    assert rejected(b"x", TXT) == ("parse_error", "The file could not be read.")


def test_a_child_that_hangs_is_killed_at_the_timeout(monkeypatch: pytest.MonkeyPatch) -> None:
    child(monkeypatch, "import time; time.sleep(60)")
    started = time.monotonic()

    assert rejected(b"x", TXT, timeout_s=1) == ("parse_error", "The file took too long to read.")
    assert time.monotonic() - started < 10


@pytest.mark.skipif(sys.platform == "win32", reason="Windows has no memory limit (sandbox.py)")
def test_a_child_over_the_memory_limit_fails_with_parse_error(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    child(monkeypatch, reply("str(len(bytearray(600 * 1024 * 1024)))"))

    # The same child fits under a higher limit: the limit is what stops it.
    assert rejected(b"x", TXT, memory_mb=256)[0] == "parse_error"
    assert parse(b"x", TXT, memory_mb=4096).parts[0].text == str(600 * 1024 * 1024)


def test_the_child_environment_holds_no_credentials(monkeypatch: pytest.MonkeyPatch) -> None:
    secrets = {
        "NORMETRY_DATABASE_URL": "postgresql+psycopg://app_ai:pw@db:5432/normetry",
        "NORMETRY_BLOB_CONNECTION_STRING": "DefaultEndpointsProtocol=https;AccountKey=a2V5",
        "NORMETRY_RABBITMQ_URL": "amqp://normetry:pw@broker:5672/",
        "NORMETRY_INTERNAL_TOKEN": "t" * 32,
        "ConnectionStrings__documents": "BlobEndpoint=http://blobs:10000/;SharedAccessSignature=s",
        "DATABASE_URL": "postgresql://user:pw@db/x",
    }
    for name, value in secrets.items():
        monkeypatch.setenv(name, value)
    child(monkeypatch, reply("json.dumps(dict(os.environ))"))

    environment = json.loads(parse(b"x", TXT).parts[0].text)

    assert not [name for name in environment if name.upper().startswith("NORMETRY_")]
    assert not set(environment) & ({name.upper() for name in secrets} | set(secrets))
    assert not [value for value in environment.values() if "://" in value or "Key=" in value]


def test_the_worker_imports_no_settings_database_storage_or_broker() -> None:
    code = (
        "import sys, normetry_ai.ingestion.parse_worker; "
        "print(' '.join(name for name in sys.modules if name.split('.')[0] in "
        "{'sqlalchemy', 'psycopg', 'azure', 'aio_pika', 'aiormq', 'pydantic_settings'} "
        "or name in {'normetry_ai.settings', 'normetry_ai.db'}))"
    )
    loaded = subprocess.run(  # noqa: S603
        [sys.executable, "-I", "-c", code],
        capture_output=True,
        text=True,
        env=sandbox.child_environment(),
        check=True,
    )

    assert loaded.stdout.split() == []


@pytest.mark.integration
def test_the_worker_moves_on_after_a_parser_crash(
    app_ai: Engine, monkeypatch: pytest.MonkeyPatch
) -> None:
    tenant, container = uuid4(), FakeContainer()
    worker = DocumentWorker(app_ai, FakeEmbedder(), BlobStore(container), LIMITS)

    def upload(data: bytes) -> DocumentUploaded:
        version, path = uuid4(), f"tenants/{tenant}/{uuid4()}.txt"
        container.blobs[path] = data
        return DocumentUploaded(
            establishment_id=uuid4(),
            document_id=uuid4(),
            version_id=version,
            version_number=1,
            blob_path=path,
            content_type=TXT,
            size=len(data),
            sha256=hashlib.sha256(data).hexdigest(),
        )

    original = sandbox.COMMAND
    child(monkeypatch, "import os; os.abort()")
    with pytest.raises(Rejected) as error:
        worker.ingest(tenant, upload(LINE.encode()), lambda: None)
    assert error.value.reason == "parse_error"

    monkeypatch.setattr(sandbox, "COMMAND", original)
    indexed = worker.ingest(tenant, upload(LINE.encode()), lambda: None)
    assert indexed is not None and indexed.chunk_count == 1
