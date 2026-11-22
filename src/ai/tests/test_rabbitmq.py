"""The consumer against a real RabbitMQ (Testcontainers) and the shared PostgreSQL, with a fake blob
store and embedder: topology, AMQP properties, trace propagation, quorum-queue delivery counting
and dead-lettering. Skipped without Docker, except in CI (`CI` set)."""

import asyncio
import hashlib
import os
import secrets
from collections.abc import Iterator
from typing import Any
from uuid import UUID, uuid4

import aio_pika
import pytest
from conftest import TOKEN
from docker.errors import DockerException
from fakes import FakeContainer, FakeEmbedder
from fastapi.testclient import TestClient
from sample_files import LINE
from sqlalchemy import Engine
from test_inspection_consumer import FakeWorker
from testcontainers.core.container import DockerContainer
from testcontainers.core.wait_strategies import LogMessageWaitStrategy

from normetry_ai.api import create_app
from normetry_ai.ingestion.documents import BlobStore, DocumentWorker
from normetry_ai.messaging import consumer, delivery, inspections
from normetry_ai.messaging.contracts import (
    DocumentIndexed,
    DocumentIngestionFailed,
    DocumentIngestionStarted,
    DocumentUploaded,
    InspectionCompleted,
    InspectionFailed,
    InspectionProgressed,
    InspectionRequested,
    InspectionStarted,
    envelope,
    parse,
)
from normetry_ai.settings import Settings

pytestmark = pytest.mark.integration

IMAGE = "rabbitmq:4.3-alpine"
RESULT_KEYS = [
    payload.ROUTING_KEY
    for payload in (DocumentIngestionStarted, DocumentIndexed, DocumentIngestionFailed)
]
TRACE_ID = "4bf92f3577b34da6a3ce929d0e0e4736"


@pytest.fixture(scope="module")
def amqp_url() -> Iterator[str]:
    password = secrets.token_hex(16)
    container = (
        DockerContainer(IMAGE)
        .with_exposed_ports(5672)
        .with_env("RABBITMQ_DEFAULT_USER", "normetry")
        .with_env("RABBITMQ_DEFAULT_PASS", password)
        .waiting_for(LogMessageWaitStrategy("Server startup complete").with_startup_timeout(90))
    )
    try:
        container.start()
    except DockerException as error:
        if os.environ.get("CI"):
            raise
        pytest.skip(f"Docker is not available: {error}")
    try:
        host, port = container.get_container_host_ip(), container.get_exposed_port(5672)
        yield f"amqp://normetry:{password}@{host}:{port}/"
    finally:
        container.stop()


def upload(blobs: FakeContainer, tenant: UUID, data: bytes) -> DocumentUploaded:
    """A DocumentUploaded for a new document, its file put where the core would put it."""
    establishment, document, version = uuid4(), uuid4(), uuid4()
    path = (
        f"tenants/{tenant}/establishments/{establishment}/documents/{document}/versions/{version}/f"
    )
    blobs.blobs[path] = data
    return DocumentUploaded(
        establishment_id=establishment,
        document_id=document,
        version_id=version,
        version_number=1,
        blob_path=path,
        content_type="text/plain",
        size=len(data),
        sha256=hashlib.sha256(data).hexdigest(),
    )


INSPECTION_KEYS = [
    payload.ROUTING_KEY
    for payload in (InspectionStarted, InspectionProgressed, InspectionCompleted, InspectionFailed)
]


async def exchange(
    url: str,
    outgoing: list[tuple[str, bytes]],
    results: int,
    dead_letters: int,
    result_keys: list[str] = RESULT_KEYS,  # noqa: B006 (never mutated)
    dead_letter_queue_name: str = consumer.DEAD_LETTER_QUEUE,
) -> tuple[list[aio_pika.abc.AbstractIncomingMessage], list[bytes]]:
    """Publish (routing key, body) pairs as the core would, then collect `results` result events
    and `dead_letters` messages from the dead-letter queue."""
    connection = await aio_pika.connect(url)
    async with connection:
        channel = await connection.channel()
        events = await channel.declare_exchange(
            delivery.EXCHANGE, aio_pika.ExchangeType.TOPIC, durable=True
        )
        # Stands in for the core's queue: it must exist before results are published.
        inbox = await channel.declare_queue(exclusive=True)
        for key in result_keys:
            await inbox.bind(events, key)
        headers = {"traceparent": f"00-{TRACE_ID}-00f067aa0ba902b7-01"}
        for key, body in outgoing:
            message = aio_pika.Message(body, headers=headers, content_type="application/json")
            await events.publish(message, routing_key=key)
        received: list[aio_pika.abc.AbstractIncomingMessage] = []
        async with asyncio.timeout(30):
            async with inbox.iterator() as messages:
                async for message in messages:
                    await message.ack()
                    received.append(message)
                    if len(received) == results:
                        break
        dead_letter_queue = await channel.get_queue(dead_letter_queue_name)
        dead: list[bytes] = []
        async with asyncio.timeout(10):
            while len(dead) < dead_letters:
                if (message := await dead_letter_queue.get(fail=False)) is None:
                    await asyncio.sleep(0.1)  # the reject follows the last event
                    continue
                await message.ack()
                dead.append(message.body)
        return received, dead


def test_the_service_ingests_an_upload_and_reports_it_over_the_queue(
    urls: dict[str, str], amqp_url: str
) -> None:
    settings = Settings(
        internal_token=TOKEN,
        database_url=urls["app_ai"],
        rabbitmq_url=amqp_url,
        blob_connection_string="UseDevelopmentStorage=true",
        _env_file=None,
    )
    tenant, blobs = uuid4(), FakeContainer()
    message = upload(blobs, tenant, f"{LINE} {LINE}".encode())
    body = envelope(tenant, message).model_dump_json().encode()

    with TestClient(create_app(settings)) as client:  # the lifespan starts the consumer
        worker: DocumentWorker = client.app.state.documents
        # The worker shares the chat retriever's embedder (swapped here for a fake one).
        assert worker.embedder is client.app.state.retriever.embedder
        # So do the inspections, with its re-ranker too: each model loads once per process.
        inspector = client.app.state.inspections
        assert inspector.embedder is worker.embedder
        assert inspector.reranker is client.app.state.retriever.reranker
        worker.embedder, worker.blobs = FakeEmbedder(), BlobStore(blobs)
        received, dead = asyncio.run(
            exchange(
                amqp_url,
                [("document.uploaded.v1", b"not a message"), ("document.uploaded.v1", body)],
                results=2,
                dead_letters=1,
            )
        )

    assert [parse(m.body).type for m in received] == ["DocumentIngestionStarted", "DocumentIndexed"]
    indexed = parse(received[1].body)
    assert (indexed.tenant_id, indexed.payload.document_id) == (tenant, message.document_id)
    assert indexed.payload.chunk_count == 1
    for m in received:
        assert m.content_type == "application/json"
        assert m.delivery_mode == aio_pika.DeliveryMode.PERSISTENT
        assert (m.message_id, m.type) == (str(parse(m.body).message_id), parse(m.body).type)
        # The consumer's span continues the publisher's trace.
        assert m.headers["traceparent"].split("-")[1] == TRACE_ID
    # The body off its contract went straight to the dead-letter queue.
    assert dead == [b"not a message"]


class BrokenEmbedder(FakeEmbedder):
    def embed_documents(self, texts: Any) -> list[list[float]]:
        self.embedded += texts
        raise RuntimeError("model weights unavailable")


def test_a_failing_upload_gets_five_deliveries_then_fails_and_is_dead_lettered(
    app_ai: Engine, amqp_url: str
) -> None:
    tenant, blobs, embedder = uuid4(), FakeContainer(), BrokenEmbedder()
    worker = DocumentWorker(app_ai, embedder, BlobStore(blobs))
    message = upload(blobs, tenant, b"Some text to index.")
    body = envelope(tenant, message).model_dump_json().encode()

    async def run() -> tuple[list[Any], list[bytes]]:
        connection = await consumer.start(amqp_url, worker, delay=lambda _: 0)
        async with connection:
            # Five Started (one per delivery, all with the same message id), then the failure.
            return await exchange(
                amqp_url, [("document.uploaded.v1", body)], results=6, dead_letters=1
            )

    received, dead = asyncio.run(run())

    kinds = [parse(m.body).type for m in received]
    assert kinds == ["DocumentIngestionStarted"] * 5 + ["DocumentIngestionFailed"]
    assert len({m.message_id for m in received[:5]}) == 1
    assert parse(received[-1].body).payload.reason_code == "internal"
    assert len(embedder.embedded) == 1 + delivery.DELIVERY_LIMIT
    assert dead == [body]


def test_the_inspection_consumer_declares_its_queues_and_reports_over_them(
    amqp_url: str,
) -> None:
    tenant, worker = uuid4(), FakeWorker()
    request = InspectionRequested(
        establishment_id=uuid4(),
        inspection_id=uuid4(),
        jurisdiction="US",
        establishment_type="Restaurant",
        token_ceiling=1000,
        requested_by="user-1",
    )
    body = envelope(tenant, request).model_dump_json().encode()

    async def run() -> tuple[list[Any], list[bytes]]:
        connection = await inspections.start(amqp_url, worker, delay=lambda _: 0)
        async with connection:
            received, dead = await exchange(
                amqp_url,
                [("inspection.requested.v1", b"not a message"), ("inspection.requested.v1", body)],
                results=3,
                dead_letters=1,
                result_keys=INSPECTION_KEYS,
                dead_letter_queue_name=inspections.DEAD_LETTER_QUEUE,
            )
            # The contract's arguments, declared again: a mismatch closes the channel with
            # PRECONDITION_FAILED.
            channel = await connection.channel()
            await channel.declare_queue(
                "ai.inspections",
                durable=True,
                arguments={
                    "x-queue-type": "quorum",
                    "x-dead-letter-exchange": "normetry.dead-letter",
                    "x-dead-letter-routing-key": "ai.inspections",
                    "x-delivery-limit": 4,
                },
            )
            await channel.declare_queue(
                "ai.inspections.dead-letter", durable=True, arguments={"x-queue-type": "quorum"}
            )
            return received, dead

    received, dead = asyncio.run(run())

    assert [parse(m.body).type for m in received] == [
        "InspectionStarted",
        "InspectionProgressed",
        "InspectionCompleted",
    ]
    assert worker.calls == [(tenant, request)]
    for m in received:
        assert (m.message_id, m.type) == (str(parse(m.body).message_id), parse(m.body).type)
        assert m.headers["traceparent"].split("-")[1] == TRACE_ID
    assert dead == [b"not a message"]
