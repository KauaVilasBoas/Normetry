"""The document ingestion consumer: an aio-pika consumer inside the AI service process.

Topology, AMQP properties, headers and retries are the contract's
(src/shared/contracts/messages/README.md). One message at a time (prefetch 1): ingestion embeds on
the CPU the chat also uses, so a second message in parallel would only slow both down.

`handle` decides what a message gets (ack, retry or dead-letter) and publishes the result events;
`start` wires it to RabbitMQ through `consume`. The inspection consumer (`inspections.py`) shares
`consume` and the delivery template `handle_delivery` (contract check, span attributes, outage
wait, retries, last failure and dead-letter). The tenant of every transaction comes from the
message's envelope.
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
    DocumentFailureReason,
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


# What ai.documents takes: the queue is bound to their routing keys, and anything else is
# dead-lettered.
CONSUMED: tuple[type[Payload], ...] = (DocumentUploaded, DocumentDeleted)
INDEXING_ERROR = "The document could not be indexed. Try uploading it again."


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

    async def process(message: Envelope[Any]) -> Action:
        if isinstance(message.payload, DocumentDeleted):
            await anyio.to_thread.run_sync(worker.delete, message.tenant_id, message.payload)
        else:
            await _uploaded(message.tenant_id, message.payload, worker, publish)
        return "ack"

    return await handle_delivery(
        body,
        delivery_count,
        accepts=CONSUMED,
        process=process,
        reachable=worker.reachable,
        unreachable="the database or blob storage",
        subject=lambda message: f"{message.type} {message.message_id}",
        last_failure=_last_failure,
        publish=publish,
    )


def _last_failure(message: Envelope[Any], error: Exception) -> Envelope[Any] | None:
    """The DocumentIngestionFailed (`internal`) of an upload on its last delivery. None for a
    delete, and for an upload that is indexed (only its DocumentIndexed could not be sent)."""
    if not isinstance(message.payload, DocumentUploaded) or isinstance(error, IndexedNotPublished):
        return None
    failed = DocumentIngestionFailed(
        **_version(message.payload),
        reason_code=DocumentFailureReason.INTERNAL,
        reason_text=INDEXING_ERROR,
    )
    return event(message.tenant_id, failed)


async def handle_delivery(
    body: bytes,
    delivery_count: int,
    *,
    accepts: tuple[type[Payload], ...],
    process: Callable[[Envelope[Any]], Awaitable[Action]],
    reachable: Callable[[], bool],
    unreachable: str,
    subject: Callable[[Envelope[Any]], str],
    last_failure: Callable[[Envelope[Any], Exception], Envelope[Any] | None],
    publish: Publish,
) -> Action:
    """What one delivery gets, for every consumed queue; each queue's `handle` brings its own work.

    A body that breaks the contract, or a payload that is not one of `accepts`, is dead-lettered
    at once. Otherwise `process` runs and its action is returned. When it raises, the message
    waits while `reachable` (run in a worker thread) is false: the database or storage is down,
    which is not the message's fault, and `unreachable` names it in the log. Otherwise it is
    retried; on the last delivery the event `last_failure` gives (if any) is published first, so
    the core does not wait for its timeout, then the message is dead-lettered. `subject` names
    the message in the failure log (ids only).
    """
    message = _parsed(body)
    if message is None:
        return "dead-letter"
    # So traces can be audited and filtered per tenant (ids only, never names or text).
    trace.get_current_span().set_attributes(
        {"normetry.tenant_id": str(message.tenant_id), "normetry.message.type": message.type}
    )
    if not isinstance(message.payload, accepts):
        logger.warning(
            "Dead-lettered a %s of tenant %s, which this queue does not take",
            message.type,
            message.tenant_id,
            extra={"tenant_id": str(message.tenant_id)},
        )
        return "dead-letter"
    try:
        return await process(message)
    except Exception as error:
        if not await anyio.to_thread.run_sync(reachable):
            _log_held(message, unreachable, error)
            return "wait"
        last = delivery_count >= DELIVERY_LIMIT
        _log_failed_delivery(message, subject(message), delivery_count, error, last)
        if not last:
            return "retry"
        failure = last_failure(message, error)
        if failure is not None:
            await _publish_last_failure(failure, publish)
        return "dead-letter"


def _parsed(body: bytes) -> Envelope[Any] | None:
    """The message, or None when the body breaks the contract (logged by error type only)."""
    try:
        return parse(body)
    except ValueError as error:
        logger.warning("Dead-lettered a message off its contract (%s)", type(error).__name__)
        return None


def _log_held(message: Envelope[Any], unreachable: str, error: Exception) -> None:
    logger.warning(
        "Holding %s %s of tenant %s: %s is unreachable (%s)",
        message.type,
        message.message_id,
        message.tenant_id,
        unreachable,
        type(error).__name__,
        extra={"tenant_id": str(message.tenant_id)},
    )


def _log_failed_delivery(
    message: Envelope[Any], subject: str, delivery_count: int, error: Exception, last: bool
) -> None:
    # The error type only: a database or model error's text can quote stored rows.
    logger.warning(
        "Delivery %d of %s of tenant %s failed (%s)%s",
        delivery_count + 1,
        subject,
        message.tenant_id,
        type(error).__name__,
        ", dead-lettered" if last else ", retrying",
        extra={"tenant_id": str(message.tenant_id)},
    )


async def _publish_last_failure(failure: Envelope[Any], publish: Publish) -> None:
    try:
        await publish(failure)
    except Exception as publish_error:  # the core's timeout fails it instead
        logger.warning(
            "Could not publish the failure for tenant %s (%s)",
            failure.tenant_id,
            type(publish_error).__name__,
            extra={"tenant_id": str(failure.tenant_id)},
        )


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

    return await consume(url, QUEUE, CONSUMED, handler, delay)


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
