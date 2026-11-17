"""The document ingestion consumer: an aio-pika consumer inside the AI service process.

Topology, AMQP properties, headers and retries are the contract's
(src/shared/contracts/messages/README.md). One message at a time (prefetch 1): ingestion embeds on
the CPU the chat also uses, so a second message in parallel would only slow both down.

`handle` decides what a message gets (ack, retry or dead-letter) and publishes the result events;
`start` wires it to RabbitMQ through `consume`, which the inspection consumer (`inspections.py`)
shares. The tenant of every transaction comes from the message's envelope.
"""

import asyncio
import logging
from collections.abc import Awaitable, Callable, Iterable, Mapping
from typing import Any, Literal
from uuid import NAMESPACE_URL, UUID, uuid5

import aio_pika
import anyio
from aio_pika.abc import AbstractIncomingMessage, AbstractRobustConnection
from opentelemetry import propagate, trace

from normetry_ai.ingestion.documents import DocumentWorker
from normetry_ai.ingestion.parsers import Rejected
from normetry_ai.messaging.contracts import (
    DocumentDeleted,
    DocumentIngestionFailed,
    DocumentIngestionStarted,
    DocumentUploaded,
    Envelope,
    Payload,
    VersionPayload,
    envelope,
    parse,
)

logger = logging.getLogger(__name__)
tracer = trace.get_tracer(__name__)

EXCHANGE = "normetry.events"
DEAD_LETTER_EXCHANGE = "normetry.dead-letter"
QUEUE = "ai.documents"
DEAD_LETTER_QUEUE = "ai.documents.dead-letter"
# Five deliveries in all: RabbitMQ dead-letters a message whose x-delivery-count passes this.
DELIVERY_LIMIT = 4


def queue_arguments(queue: str) -> dict[str, Any]:
    """The arguments of a consumed queue (the contract's topology table): quorum, dead-lettered
    to `normetry.dead-letter` with its own name after DELIVERY_LIMIT redeliveries."""
    return {
        "x-queue-type": "quorum",
        "x-dead-letter-exchange": DEAD_LETTER_EXCHANGE,
        "x-dead-letter-routing-key": queue,
        "x-delivery-limit": DELIVERY_LIMIT,
    }


QUEUE_ARGUMENTS = queue_arguments(QUEUE)
# Result events get an id derived from the version, so the core's inbox drops a re-published one.
EVENT_IDS = uuid5(NAMESPACE_URL, "https://contracts.normetry.example/messages/")

Action = Literal["ack", "retry", "dead-letter", "wait"]
# While the database or blob storage is down, a message waits here, unacknowledged, and spends
# none of its deliveries. RabbitMQ's consumer timeout (30 minutes by default) bounds one wait.
OUTAGE_WAIT_SECONDS = 10.0
Publish = Callable[[Envelope[Any]], Awaitable[None]]
# A queue's `handle`: (body, delivery count, publish) -> what the delivery gets.
Handler = Callable[[bytes, int, Publish], Awaitable[Action]]


class IndexedNotPublished(Exception):
    """The version is indexed and current; only publishing its DocumentIndexed failed. The last
    delivery must not report it as failed, since search already reads it."""


def event(tenant_id: UUID, payload: VersionPayload) -> Envelope[Any]:
    """A result event for one document version, with its deterministic message id. The tenant is
    part of it as well: the core's inbox is keyed by (tenant_id, message_id), and an id that differs
    per tenant keeps two tenants' events apart wherever an id is compared alone."""
    message_id = uuid5(EVENT_IDS, f"{tenant_id}:{payload.TYPE}:{payload.version_id}")
    return envelope(tenant_id, payload, message_id)


def _version(uploaded: DocumentUploaded) -> dict[str, Any]:
    return uploaded.model_dump(
        include={"establishment_id", "document_id", "version_id", "version_number"}
    )


async def _uploaded(
    tenant_id: UUID, uploaded: DocumentUploaded, worker: DocumentWorker, publish: Publish
) -> None:
    def started() -> None:  # runs in the worker thread
        anyio.from_thread.run(
            publish, event(tenant_id, DocumentIngestionStarted(**_version(uploaded)))
        )

    try:
        result = await anyio.to_thread.run_sync(worker.ingest, tenant_id, uploaded, started)
    except Rejected as rejected:
        logger.info(
            "Version %d of document %s of tenant %s failed: %s",
            uploaded.version_number,
            uploaded.document_id,
            tenant_id,
            rejected.reason,
            extra={"tenant_id": str(tenant_id)},
        )
        failed = DocumentIngestionFailed(
            **_version(uploaded), reason_code=rejected.reason, reason_text=rejected.text
        )
        await publish(event(tenant_id, failed))
        return
    if result is not None:
        try:
            await publish(event(tenant_id, result))
        except Exception as error:
            raise IndexedNotPublished from error


async def handle(
    body: bytes, delivery_count: int, worker: DocumentWorker, publish: Publish
) -> Action:
    """Process one delivery of a message from the ai.documents queue.

    A body that breaks the contract, or a message this queue does not take, is dead-lettered at
    once. A file that cannot be indexed publishes DocumentIngestionFailed and is acknowledged.
    Any other error waits while the database or blob storage is unreachable (`wait`: the caller
    handles the message again later), and is retried otherwise; on the last delivery an upload that
    is not indexed publishes DocumentIngestionFailed (`internal`) first, so the core does not wait
    for its timeout.
    """
    try:
        message = parse(body)
    except ValueError as error:
        logger.warning("Dead-lettered a message off its contract (%s)", type(error).__name__)
        return "dead-letter"
    tenant_id, payload = message.tenant_id, message.payload
    # So traces can be audited and filtered per tenant (ids only, never names or text).
    trace.get_current_span().set_attributes(
        {"normetry.tenant_id": str(tenant_id), "normetry.message.type": message.type}
    )
    if not isinstance(payload, DocumentUploaded | DocumentDeleted):
        logger.warning(
            "Dead-lettered a %s of tenant %s, which this queue does not take",
            message.type,
            tenant_id,
            extra={"tenant_id": str(tenant_id)},
        )
        return "dead-letter"
    try:
        if isinstance(payload, DocumentDeleted):
            await anyio.to_thread.run_sync(worker.delete, tenant_id, payload)
        else:
            await _uploaded(tenant_id, payload, worker, publish)
        return "ack"
    except Exception as error:
        if not await anyio.to_thread.run_sync(worker.reachable):
            logger.warning(
                "Holding %s %s of tenant %s: the database or blob storage is unreachable (%s)",
                message.type,
                message.message_id,
                tenant_id,
                type(error).__name__,
                extra={"tenant_id": str(tenant_id)},
            )
            return "wait"
        last = delivery_count >= DELIVERY_LIMIT
        # The error type only: a database error's text can quote stored rows.
        logger.warning(
            "Delivery %d of %s %s of tenant %s failed (%s)%s",
            delivery_count + 1,
            message.type,
            message.message_id,
            tenant_id,
            type(error).__name__,
            ", dead-lettered" if last else ", retrying",
            extra={"tenant_id": str(tenant_id)},
        )
        if not last:
            return "retry"
        if isinstance(payload, DocumentUploaded) and not isinstance(error, IndexedNotPublished):
            failed = DocumentIngestionFailed(
                **_version(payload),
                reason_code="internal",
                reason_text="The document could not be indexed. Try uploading it again.",
            )
            try:
                await publish(event(tenant_id, failed))
            except Exception as publish_error:  # the core's timeout will fail the version instead
                logger.warning(
                    "Could not publish the failure for tenant %s (%s)",
                    tenant_id,
                    type(publish_error).__name__,
                    extra={"tenant_id": str(tenant_id)},
                )
        return "dead-letter"


RETRY_SECONDS = (2, 10, 30, 60)


def retry_delay(delivery_count: int) -> float:
    """Seconds to wait before requeueing: 2, 10, 30, then 60. The 102 seconds in all outlast a
    database or blob storage restart; a dead-lettered message is not replayed."""
    return float(RETRY_SECONDS[min(delivery_count, len(RETRY_SECONDS) - 1)])


def _delivery_count(headers: Mapping[str, Any]) -> int:
    # Set by quorum queues on redeliveries; a basic.reject and a lost delivery count, a nack not.
    count = headers.get("x-delivery-count", 0)
    return count if isinstance(count, int) else 0


async def start(
    url: str, worker: DocumentWorker, delay: Callable[[int], float] = retry_delay
) -> AbstractRobustConnection:
    """Connect, declare the topology and start consuming ai.documents. The caller closes the
    connection."""

    async def handler(body: bytes, delivery_count: int, publish: Publish) -> Action:
        return await handle(body, delivery_count, worker, publish)

    return await consume(url, QUEUE, (DocumentUploaded, DocumentDeleted), handler, delay)


async def consume(
    url: str,
    queue_name: str,
    consumed: Iterable[type[Payload]],
    handler: Handler,
    delay: Callable[[int], float] = retry_delay,
) -> AbstractRobustConnection:
    """Open a connection of its own, declare both exchanges, `queue_name` (bound to the routing
    keys of `consumed`) and its dead-letter queue, and consume it one message at a time. The
    caller closes the connection."""
    dead_letter_name = f"{queue_name}.dead-letter"
    connection = await aio_pika.connect_robust(url)
    try:
        # Publisher confirms, and a mandatory publish the broker cannot route raises: a result
        # event never counts as sent before the core's queue holds it.
        channel = await connection.channel(publisher_confirms=True, on_return_raises=True)
        await channel.set_qos(prefetch_count=1)
        events = await channel.declare_exchange(EXCHANGE, aio_pika.ExchangeType.TOPIC, durable=True)
        dead_letters = await channel.declare_exchange(
            DEAD_LETTER_EXCHANGE, aio_pika.ExchangeType.DIRECT, durable=True
        )
        queue = await channel.declare_queue(
            queue_name, durable=True, arguments=queue_arguments(queue_name)
        )
        for payload_type in consumed:
            await queue.bind(events, payload_type.ROUTING_KEY)
        dead_letter_queue = await channel.declare_queue(
            dead_letter_name, durable=True, arguments={"x-queue-type": "quorum"}
        )
        await dead_letter_queue.bind(dead_letters, queue_name)

        async def publish(message: Envelope[Any]) -> None:
            headers: dict[str, Any] = {}
            propagate.inject(headers)  # traceparent of the current span
            payload: Payload = message.payload
            await events.publish(
                aio_pika.Message(
                    message.model_dump_json().encode(),
                    content_type="application/json",
                    delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
                    message_id=str(message.message_id),
                    type=message.type,
                    headers=headers,
                ),
                routing_key=payload.ROUTING_KEY,
                mandatory=True,
            )

        async def on_message(incoming: AbstractIncomingMessage) -> None:
            headers = incoming.headers or {}
            count = _delivery_count(headers)
            parent = propagate.extract({k: v for k, v in headers.items() if isinstance(v, str)})
            with tracer.start_as_current_span(
                f"process {queue_name}",
                context=parent,
                kind=trace.SpanKind.CONSUMER,
                attributes={
                    "messaging.system": "rabbitmq",
                    "messaging.destination.name": queue_name,
                    "messaging.message.id": incoming.message_id or "",
                },
            ):
                action = await handler(incoming.body, count, publish)
                while action == "wait":
                    await asyncio.sleep(OUTAGE_WAIT_SECONDS)
                    action = await handler(incoming.body, count, publish)
            if action == "ack":
                await incoming.ack()
            elif action == "retry":
                await asyncio.sleep(delay(count))
                await incoming.reject(requeue=True)
            else:
                await incoming.reject(requeue=False)

        await queue.consume(on_message)
    except BaseException:
        await connection.close()
        raise
    logger.info("Consuming %s", queue_name)
    return connection
