"""The document ingestion consumer: an aio-pika consumer inside the AI service process.

One message at a time (prefetch 1): ingestion embeds on the CPU the chat also uses, so a second
message in parallel would only slow both down.

`handle` decides what a message gets (ack, retry or dead-letter) and publishes the result events;
`start` wires it to RabbitMQ through `consume`. The topology, retries, publishing and the delivery
template `handle_delivery` are shared with the inspection consumer (`delivery.py`). The tenant of
every transaction comes from the message's envelope.
"""

import logging
from collections.abc import Callable
from typing import Any
from uuid import UUID, uuid5

import anyio
from aio_pika.abc import AbstractRobustConnection

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
)
from normetry_ai.messaging.delivery import (
    EVENT_IDS,
    DeliveryAction,
    Publish,
    consume,
    handle_delivery,
    queue_arguments,
    retry_delay,
)

logger = logging.getLogger(__name__)

QUEUE = "ai.documents"
DEAD_LETTER_QUEUE = "ai.documents.dead-letter"
QUEUE_ARGUMENTS = queue_arguments(QUEUE)
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
) -> DeliveryAction:
    """Process one delivery of a message from the ai.documents queue.

    A body that breaks the contract, or a message this queue does not take, is dead-lettered at
    once. A file that cannot be indexed publishes DocumentIngestionFailed and is acknowledged.
    Any other error waits while the database or blob storage is unreachable (`wait`: the caller
    handles the message again later), and is retried otherwise; on the last delivery an upload that
    is not indexed publishes DocumentIngestionFailed (`internal`) first, so the core does not wait
    for its timeout.
    """

    async def process(message: Envelope[Any]) -> DeliveryAction:
        if isinstance(message.payload, DocumentDeleted):
            await anyio.to_thread.run_sync(worker.delete, message.tenant_id, message.payload)
        else:
            await _uploaded(message.tenant_id, message.payload, worker, publish)
        return DeliveryAction.ACK

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


async def start(
    url: str, worker: DocumentWorker, delay: Callable[[int], float] = retry_delay
) -> AbstractRobustConnection:
    """Connect, declare the topology and start consuming ai.documents. The caller closes the
    connection."""

    async def handler(body: bytes, delivery_count: int, publish: Publish) -> DeliveryAction:
        return await handle(body, delivery_count, worker, publish)

    return await consume(url, QUEUE, CONSUMED, handler, delay)
