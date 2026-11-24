"""No document text, quote, title, file name, question or answer in a log record or a span (#111).

One document carries a marker name in its title, its file name (the blob path) and its text. It
goes through the document consumer (`consumer.handle`, the real `DocumentWorker` and parsing
subprocess), a chat turn over the real app (real retrieval with fake models, a claim model that
quotes it, the question being its text) and the inspection consumer (`inspections.handle`, the
real `InspectionWorker`, a judge that quotes it). Every log record is captured at DEBUG and every
span in an in-memory exporter, through the app's own `telemetry.instrument`. The marker must
reach the answer and the findings, and none of the records or spans.
"""

import hashlib
import logging
import re
from collections.abc import AsyncGenerator
from typing import Any
from uuid import uuid4
from xml.sax.saxutils import unescape

import anyio
import pytest
from conftest import TOKEN
from fakes import EchoJudge, FakeBroker, FakeContainer, FakeEmbedder, FakeReranker
from fastapi.testclient import TestClient
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import SimpleSpanProcessor
from opentelemetry.sdk.trace.export.in_memory_span_exporter import InMemorySpanExporter
from sqlalchemy import Engine
from test_database import ANSWERED, CHAT_SETTINGS, ChatApi, Person
from test_inspection_graph import ITEMS, SETTINGS, stub_regulation

from normetry_ai import telemetry
from normetry_ai.api import create_app
from normetry_ai.api.conversations import get_retriever
from normetry_ai.ingestion.documents import BlobStore, DocumentWorker
from normetry_ai.ingestion.normalize import normalize_text
from normetry_ai.ingestion.parsers import TXT
from normetry_ai.inspection.worker import InspectionWorker
from normetry_ai.messaging import consumer, inspections
from normetry_ai.messaging.contracts import (
    DocumentUploaded,
    InspectionCompleted,
    InspectionRequested,
    envelope,
)
from normetry_ai.retrieval.hybrid import Retriever
from normetry_ai.settings import InspectionSettings, RetrievalSettings, Settings

pytestmark = pytest.mark.integration

MARKER = "Zephyrine Quolmsby"
# The document's text, and the question word for word, so the fake re-ranker scores it 1.0.
TEXT = f"{MARKER} checks the walk in cooler every morning and writes the reading in the log."
_EXCERPT = re.compile(r'<excerpt id="(E\d+)" [^\n]*>\n(.*?)\n</excerpt>', re.DOTALL)


class QuotingClaims:
    """A ClaimModel whose claims repeat the first six words of each excerpt and cite them."""

    async def stream_claims(self, system: str, user: str) -> AsyncGenerator[Any, None]:
        for excerpt_id, content in _EXCERPT.findall(user):
            quote = " ".join(normalize_text(unescape(content)).split()[:6])
            yield {"text": quote, "citations": [{"excerpt_id": excerpt_id, "quote": quote}]}
        yield ANSWERED


@pytest.fixture(scope="module")
def regulation(corpus: Engine) -> None:
    stub_regulation(corpus, ITEMS)


def test_the_marker_never_reaches_a_log_record_or_a_span(
    urls: dict[str, str],
    app_ai: Engine,
    regulation: None,
    caplog: pytest.LogCaptureFixture,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # The app's telemetry, exporting to memory instead of OTLP, without a global provider.
    exporter, providers = InMemorySpanExporter(), list[TracerProvider]()
    monkeypatch.setenv("OTEL_EXPORTER_OTLP_ENDPOINT", "http://collector.invalid:4317")
    monkeypatch.setattr(telemetry, "OTLPSpanExporter", lambda: exporter)
    monkeypatch.setattr(telemetry, "BatchSpanProcessor", SimpleSpanProcessor)
    monkeypatch.setattr(telemetry.trace, "set_tracer_provider", providers.append)
    caplog.set_level(logging.DEBUG)
    settings = Settings(internal_token=TOKEN, database_url=urls["app_ai"], _env_file=None)
    caller = Person(uuid4(), "user-a", uuid4())

    with TestClient(create_app(settings, answer_settings=CHAT_SETTINGS)) as client:
        [provider] = providers
        tracer = provider.get_tracer(__name__)

        # Ingestion, as the document consumer handles an upload.
        document, version = uuid4(), uuid4()
        data = TEXT.encode()
        path = (
            f"tenants/{caller.tenant}/establishments/{caller.establishment}/documents/{document}"
            f"/{version}/{MARKER} cooler log.txt"
        )
        uploaded = DocumentUploaded(
            establishment_id=caller.establishment,
            document_id=document,
            version_id=version,
            version_number=1,
            blob_path=path,
            content_type=TXT,
            size=len(data),
            sha256=hashlib.sha256(data).hexdigest(),
            title=f"Cooler log kept by {MARKER}",
            document_type="Log",
        )
        documents = DocumentWorker(app_ai, FakeEmbedder(), BlobStore(FakeContainer({path: data})))
        broker = FakeBroker()
        body = envelope(caller.tenant, uploaded).model_dump_json().encode()
        with tracer.start_as_current_span("process ai.documents"):
            action = anyio.run(consumer.handle, body, 0, documents, broker.publish)
        assert (action, [kind for kind, _ in broker.sent]) == (
            "ack",
            ["DocumentIngestionStarted", "DocumentIndexed"],
        )

        # A chat turn whose question is the document's text, answered by quoting it.
        chat = ChatApi(client)
        chat.use(model=QuotingClaims())
        del client.app.dependency_overrides[get_retriever]
        client.app.state.retriever = Retriever(
            FakeEmbedder(), FakeReranker(), RetrievalSettings(_env_file=None)
        )
        conversation = chat.create(caller)
        events = chat.events(caller, conversation, TEXT)
        assert events[-1] == ("done", {})
        shown = [c for name, data in events if name == "claim" for c in data["citations"]]
        assert any(MARKER in c["quote"] and c["sourceType"] == "document" for c in shown)
        # Read back: the list's title is the question, the stored answer quotes the document.
        assert MARKER in chat.conversations(caller)[0]["title"]
        assert MARKER in chat.get(caller, conversation).text

        # An inspection of the establishment, as the inspection consumer handles a request.
        inspector = InspectionWorker(
            app_ai,
            FakeEmbedder(),
            FakeReranker(),
            EchoJudge(),
            answers=SETTINGS,
            retrieval=RetrievalSettings(_env_file=None),
            inspection=InspectionSettings(_env_file=None),
        )
        request = InspectionRequested(
            establishment_id=caller.establishment,
            inspection_id=uuid4(),
            jurisdiction="US",
            establishment_type="Restaurant",
            token_ceiling=300_000,
            requested_by=caller.user,
        )
        broker = FakeBroker()
        body = envelope(caller.tenant, request).model_dump_json().encode()
        with tracer.start_as_current_span("process ai.inspections"):
            action = anyio.run(inspections.handle, body, 0, inspector, broker.publish)
        assert action == "ack"
        completed = broker.sent[-1][1]
        assert isinstance(completed, InspectionCompleted)
        quotes = [c.quote for f in completed.findings for c in f.evidence_citations]
        assert any(MARKER in quote for quote in quotes)

    spans = exporter.get_finished_spans()
    # The app's request spans and both consumer spans were recorded, and logs were captured.
    assert {"process ai.documents", "process ai.inspections"} <= {span.name for span in spans}
    assert any(span.attributes.get("http.route") for span in spans)
    assert any(record.name.startswith("normetry_ai.") for record in caplog.records)
    # The chat's retrieval line names the tenant and establishment, as the other lines do.
    [retrieval] = [r for r in caplog.records if r.name == "normetry_ai.retrieval.hybrid"]
    assert retrieval.tenant_id == str(caller.tenant)
    assert f"establishment={caller.establishment}" in retrieval.getMessage()
    leaking_spans = [span.name for span in spans if MARKER in span.to_json()]
    leaking_records = [
        f"{record.name}: {record.getMessage()}"
        for record in caplog.records
        if MARKER in record.getMessage() or MARKER in repr(vars(record))
    ]
    assert (leaking_spans, leaking_records) == ([], [])
    assert MARKER not in caplog.text
