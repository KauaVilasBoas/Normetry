"""The document consumer's decisions (`messaging/consumer.handle`), offline: a fake broker records
what is published, a fake worker stands in for the database. The worker's own ordering rules
are in test_documents.py; RabbitMQ itself in test_rabbitmq.py."""

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import anyio
import pytest
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import SimpleSpanProcessor
from opentelemetry.sdk.trace.export.in_memory_span_exporter import InMemorySpanExporter
from pydantic import ValidationError
from test_contracts import errors, schema_name

from normetry_ai.ingestion.parsers import Rejected
from normetry_ai.messaging import consumer
from normetry_ai.messaging.consumer import DELIVERY_LIMIT, handle, retry_delay
from normetry_ai.messaging.contracts import (
    DocumentDeleted,
    DocumentIndexed,
    DocumentUploaded,
    Envelope,
    envelope,
)
from normetry_ai.settings import Settings

FIXTURES = Path(__file__).resolve().parents[2] / "shared" / "contracts" / "messages" / "fixtures"
TENANT = uuid4()
UPLOADED = DocumentUploaded(
    establishment_id=uuid4(),
    document_id=uuid4(),
    version_id=uuid4(),
    version_number=2,
    blob_path=f"tenants/{TENANT}/file.txt",
    content_type="text/plain",
    size=10,
    sha256="0" * 64,
)
INDEXED = DocumentIndexed(
    **UPLOADED.model_dump(
        include={"establishment_id", "document_id", "version_id", "version_number"}
    ),
    chunk_count=3,
    page_count=None,
    language="pt",
)
DELETED = DocumentDeleted(establishment_id=uuid4(), document_id=uuid4())


@dataclass
class FakeWorker:
    """Scripted: calls on_start unless the work is dropped, then raises or returns."""

    result: DocumentIndexed | None = None
    error: Exception | None = None
    starts: bool = True
    calls: list[tuple[str, UUID, Any]] = field(default_factory=list)

    def ingest(self, tenant_id: UUID, message: DocumentUploaded, on_start: Any) -> Any:
        self.calls.append(("ingest", tenant_id, message))
        if self.starts:
            on_start()
        if self.error is not None:
            raise self.error
        return self.result

    def delete(self, tenant_id: UUID, message: DocumentDeleted) -> None:
        self.calls.append(("delete", tenant_id, message))
        if self.error is not None:
            raise self.error


@dataclass
class FakeBroker:
    published: list[Envelope[Any]] = field(default_factory=list)
    failing: bool = False
    refuses: str | None = None  # one message type that cannot be published

    async def publish(self, message: Envelope[Any]) -> None:
        if self.failing or message.type == self.refuses:
            raise ConnectionError("broker gone")
        self.published.append(message)

    @property
    def sent(self) -> list[tuple[str, Any]]:
        return [(message.type, message.payload) for message in self.published]


def deliver(
    worker: FakeWorker, payload: Any, delivery_count: int = 0, broker: FakeBroker | None = None
) -> tuple[str, FakeBroker]:
    broker = broker or FakeBroker()
    body = envelope(TENANT, payload).model_dump_json().encode()
    action = anyio.run(handle, body, delivery_count, worker, broker.publish)
    return action, broker


def test_an_upload_publishes_started_then_its_result_for_the_message_tenant() -> None:
    worker = FakeWorker(result=INDEXED)

    action, broker = deliver(worker, UPLOADED)

    assert action == "ack"
    assert worker.calls == [("ingest", TENANT, UPLOADED)]
    assert [kind for kind, _ in broker.sent] == ["DocumentIngestionStarted", "DocumentIndexed"]
    assert broker.sent[1][1] == INDEXED
    assert {message.tenant_id for message in broker.published} == {TENANT}
    started = broker.published[0].payload
    assert (started.version_id, started.version_number) == (UPLOADED.version_id, 2)


def test_the_consumer_span_names_the_message_tenant() -> None:
    exporter = InMemorySpanExporter()
    provider = TracerProvider()
    provider.add_span_processor(SimpleSpanProcessor(exporter))
    body = envelope(TENANT, DELETED).model_dump_json().encode()

    with provider.get_tracer(__name__).start_as_current_span("process ai.documents"):
        anyio.run(handle, body, 0, FakeWorker(), FakeBroker().publish)

    (span,) = exporter.get_finished_spans()
    assert span.attributes["normetry.tenant_id"] == str(TENANT)
    assert span.attributes["normetry.message.type"] == "DocumentDeleted"


def test_result_events_have_ids_derived_from_their_tenant_and_version() -> None:
    first = deliver(FakeWorker(result=INDEXED), UPLOADED)[1].published
    again = deliver(FakeWorker(result=INDEXED), UPLOADED)[1].published

    assert [m.message_id for m in first] == [m.message_id for m in again]
    assert len({m.message_id for m in first}) == 2
    # The same version id under another tenant never shares an id in the core's inbox.
    other = consumer.event(uuid4(), first[1].payload)
    assert other.message_id != first[1].message_id


def test_a_file_that_cannot_be_indexed_publishes_its_failure_and_is_acked() -> None:
    worker = FakeWorker(error=Rejected("no_text_layer", "The PDF has no text layer."))

    action, broker = deliver(worker, UPLOADED)

    assert action == "ack"
    assert [kind for kind, _ in broker.sent] == [
        "DocumentIngestionStarted",
        "DocumentIngestionFailed",
    ]
    failed = broker.sent[1][1]
    assert (failed.reason_code, failed.reason_text) == (
        "no_text_layer",
        "The PDF has no text layer.",
    )
    assert failed.version_id == UPLOADED.version_id


def test_dropped_work_publishes_nothing_and_a_redelivered_version_its_result_again() -> None:
    # Deleted document or older version: the worker returns None before starting.
    action, broker = deliver(FakeWorker(starts=False), UPLOADED)
    assert (action, broker.sent) == ("ack", [])
    # Already current: its DocumentIndexed again, in case the first one was lost.
    action, broker = deliver(FakeWorker(result=INDEXED, starts=False), UPLOADED)
    assert (action, broker.sent) == ("ack", [("DocumentIndexed", INDEXED)])


def test_a_delete_is_applied_for_the_message_tenant() -> None:
    worker = FakeWorker()

    action, broker = deliver(worker, DELETED)

    assert (action, broker.sent) == ("ack", [])
    assert worker.calls == [("delete", TENANT, DELETED)]


@pytest.mark.parametrize(
    "body",
    [
        b"not json",
        b'{"type": "DocumentUploaded", "version": 1}',
        b'{"type": "DocumentUploaded", "version": 9, "payload": {}}',
        b'{"type": ["DocumentUploaded"], "version": {"v": 1}, "payload": {}}',
        (FIXTURES / "document-uploaded.v1.invalid.json").read_bytes(),
        (FIXTURES / "document-deleted.v1.invalid.json").read_bytes(),  # nil tenant
        (FIXTURES / "document-indexed.v1.valid.json").read_bytes(),  # not for this queue
    ],
    ids=[
        "not-json",
        "no-payload",
        "unknown-version",
        "unhashable-type",
        "no-sha256",
        "nil-tenant",
        "wrong-queue",
    ],
)
def test_a_message_off_this_queues_contract_is_dead_lettered_untouched(body: bytes) -> None:
    worker, broker = FakeWorker(), FakeBroker()

    assert anyio.run(handle, body, 0, worker, broker.publish) == "dead-letter"
    assert (worker.calls, broker.sent) == ([], [])


def test_a_transient_failure_is_retried_then_fails_the_version_and_is_dead_lettered() -> None:
    for count in range(DELIVERY_LIMIT):
        action, broker = deliver(FakeWorker(error=OSError("database down")), UPLOADED, count)
        assert action == "retry"
        assert [kind for kind, _ in broker.sent] == ["DocumentIngestionStarted"]

    action, broker = deliver(FakeWorker(error=OSError("database down")), UPLOADED, DELIVERY_LIMIT)

    assert action == "dead-letter"
    failed = broker.sent[-1][1]
    assert (broker.sent[-1][0], failed.reason_code) == ("DocumentIngestionFailed", "internal")


def test_a_failed_delete_is_retried_then_dead_lettered() -> None:
    worker = FakeWorker(error=OSError("database down"))

    assert deliver(worker, DELETED, 0) == ("retry", FakeBroker())
    assert deliver(worker, DELETED, DELIVERY_LIMIT) == ("dead-letter", FakeBroker())


def test_a_broker_failure_is_retried_and_never_blocks_the_dead_letter() -> None:
    # The Started event cannot be published: retried like any other transient failure.
    assert deliver(FakeWorker(result=INDEXED), UPLOADED, 0, FakeBroker(failing=True))[0] == "retry"
    # On the last delivery the failure event cannot go either; the message is dead-lettered.
    last = deliver(FakeWorker(result=INDEXED), UPLOADED, DELIVERY_LIMIT, FakeBroker(failing=True))
    assert last[0] == "dead-letter"


def test_an_indexed_version_whose_result_cannot_be_published_is_never_reported_failed() -> None:
    broker = FakeBroker(refuses="DocumentIndexed")

    action, broker = deliver(FakeWorker(result=INDEXED), UPLOADED, DELIVERY_LIMIT, broker)

    assert action == "dead-letter"
    assert [kind for kind, _ in broker.sent] == ["DocumentIngestionStarted"]


def test_retries_wait_longer_each_time() -> None:
    assert [retry_delay(count) for count in range(DELIVERY_LIMIT)] == [1, 2, 4, 8]


def test_the_queue_dead_letters_after_the_limit_the_consumer_counts_to() -> None:
    assert consumer.QUEUE_ARGUMENTS == {
        "x-queue-type": "quorum",
        "x-dead-letter-exchange": "normetry.dead-letter",
        "x-dead-letter-routing-key": "ai.documents",
        "x-delivery-limit": DELIVERY_LIMIT,
    }


def test_the_consumer_needs_the_database_and_blob_storage() -> None:
    base = {"internal_token": "x" * 32, "_env_file": None}
    with pytest.raises(ValidationError, match="NORMETRY_DATABASE_URL and NORMETRY_BLOB"):
        Settings(**base, rabbitmq_url="amqp://localhost/")
    with pytest.raises(ValidationError, match="NORMETRY_BLOB_CONNECTION_STRING"):
        Settings(**base, rabbitmq_url="amqp://localhost/", database_url="postgresql+psycopg://")
    # Without RabbitMQ, neither is needed: no consumer starts.
    assert Settings(**base).rabbitmq_url is None


def test_published_events_validate_against_the_shared_schemas() -> None:
    broker = deliver(FakeWorker(result=INDEXED), UPLOADED)[1]
    broker.published += deliver(FakeWorker(error=Rejected("too_large", "Big.")), UPLOADED)[
        1
    ].published
    for message in broker.published:
        body = json.loads(message.model_dump_json())
        assert errors(schema_name(type(message.payload)), body) == []
