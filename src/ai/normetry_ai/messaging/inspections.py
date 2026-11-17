"""The inspection consumer: ai.inspections, inside the AI service process, next to the document
consumer (`consumer.py`, whose topology, publishing and retries it shares).

One inspection at a time, on a connection and channel of its own (prefetch 1). The message stays
unacknowledged while the inspection runs, minutes rather than seconds, and is acknowledged once
its InspectionCompleted or InspectionFailed is confirmed by the broker. A redelivery (a crash, a
lost connection, a retry) resumes from the inspection's checkpoints; one of a finished inspection
publishes its result again. The tenant of every transaction comes from the message's envelope.
"""

import logging
from collections.abc import Awaitable, Callable
from contextvars import ContextVar
from typing import Any, Protocol
from uuid import UUID, uuid5

import anyio
from aio_pika.abc import AbstractRobustConnection
from opentelemetry import trace

from normetry_ai.inspection.worker import InspectionMismatch
from normetry_ai.messaging.consumer import (
    DELIVERY_LIMIT,
    EVENT_IDS,
    Action,
    Publish,
    consume,
    queue_arguments,
    retry_delay,
)
from normetry_ai.messaging.contracts import (
    Envelope,
    InspectionCompleted,
    InspectionFailed,
    InspectionPayload,
    InspectionProgressed,
    InspectionRequested,
    Payload,
    envelope,
    parse,
)

logger = logging.getLogger(__name__)

QUEUE = "ai.inspections"
DEAD_LETTER_QUEUE = "ai.inspections.dead-letter"
QUEUE_ARGUMENTS = queue_arguments(QUEUE)
ERROR = "The inspection could not be completed. Try running it again."

# The envelope's tenant while a delivery is handled. The judge and the Claude adapter log without
# a tenant of their own; this filter stamps it on their records as the `tenant_id` field.
TENANT_ID: ContextVar[str | None] = ContextVar("tenant_id", default=None)


class _TenantFilter(logging.Filter):
    def filter(self, record: logging.LogRecord) -> bool:
        tenant = TENANT_ID.get()
        if tenant is not None and not hasattr(record, "tenant_id"):
            record.tenant_id = tenant
        return True


for _name in ("normetry_ai.inspection.judge", "normetry_ai.llm.claude"):
    logging.getLogger(_name).addFilter(_TenantFilter())


class Worker(Protocol):
    """InspectionWorker (inspection/worker.py), or a fake in tests."""

    def reachable(self) -> bool: ...

    async def inspect(
        self,
        tenant_id: UUID,
        request: InspectionRequested,
        publish: Callable[[Payload], Awaitable[None]],
    ) -> InspectionCompleted | InspectionFailed: ...


def event(tenant_id: UUID, payload: InspectionPayload) -> Envelope[Any]:
    """A result event with its deterministic message id: UUIDv5 of the tenant id, type and
    inspection id, plus itemsEvaluated for InspectionProgressed (each step its own id). The core's
    inbox drops a re-published one; two tenants' events never share an id."""
    name = f"{tenant_id}:{payload.TYPE}:{payload.inspection_id}"
    if isinstance(payload, InspectionProgressed):
        name += f":{payload.items_evaluated}"
    return envelope(tenant_id, payload, uuid5(EVENT_IDS, name))


async def handle(body: bytes, delivery_count: int, worker: Worker, publish: Publish) -> Action:
    """Process one delivery of a message from the ai.inspections queue.

    A body that breaks the contract, or a message this queue does not take, is dead-lettered at
    once, and so is a request whose inspection id already has checkpoints for another
    establishment (`InspectionMismatch`), with nothing published. The inspection's result
    (completed, or failed `budget_exceeded` or `no_documents`) is published and the message
    acknowledged. Any other error waits while the database is
    unreachable (`wait`) and is retried otherwise; the last delivery publishes InspectionFailed
    (`error`) first, so the core does not wait for its timeout.
    """
    try:
        message = parse(body)
    except ValueError as error:
        logger.warning("Dead-lettered a message off its contract (%s)", type(error).__name__)
        return "dead-letter"
    tenant_id, request = message.tenant_id, message.payload
    trace.get_current_span().set_attributes(
        {"normetry.tenant_id": str(tenant_id), "normetry.message.type": message.type}
    )
    if not isinstance(request, InspectionRequested):
        logger.warning(
            "Dead-lettered a %s of tenant %s, which this queue does not take",
            message.type,
            tenant_id,
            extra={"tenant_id": str(tenant_id)},
        )
        return "dead-letter"
    token = TENANT_ID.set(str(tenant_id))
    try:
        return await _inspect(
            tenant_id, request, message.message_id, delivery_count, worker, publish
        )
    finally:
        TENANT_ID.reset(token)


async def _inspect(
    tenant_id: UUID,
    request: InspectionRequested,
    message_id: UUID,
    delivery_count: int,
    worker: Worker,
    publish: Publish,
) -> Action:
    async def emit(payload: InspectionPayload) -> None:
        await publish(event(tenant_id, payload))

    try:
        await emit(await worker.inspect(tenant_id, request, emit))
        return "ack"
    except InspectionMismatch:
        # The core sent another establishment for an inspection id it already used: nothing to
        # resume or publish, and no retry would change that.
        logger.warning(
            "Dead-lettered inspection %s of tenant %s: its checkpoints are for another "
            "establishment",
            request.inspection_id,
            tenant_id,
            extra={"tenant_id": str(tenant_id)},
        )
        return "dead-letter"
    except Exception as error:
        if not await anyio.to_thread.run_sync(worker.reachable):
            logger.warning(
                "Holding %s %s of tenant %s: the database is unreachable (%s)",
                request.TYPE,
                message_id,
                tenant_id,
                type(error).__name__,
                extra={"tenant_id": str(tenant_id)},
            )
            return "wait"
        last = delivery_count >= DELIVERY_LIMIT
        # The error type only: a database or model error's text can quote stored rows.
        logger.warning(
            "Delivery %d of inspection %s of tenant %s failed (%s)%s",
            delivery_count + 1,
            request.inspection_id,
            tenant_id,
            type(error).__name__,
            ", dead-lettered" if last else ", retrying",
            extra={"tenant_id": str(tenant_id)},
        )
        if not last:
            return "retry"
        failed = InspectionFailed(
            establishment_id=request.establishment_id,
            inspection_id=request.inspection_id,
            reason_code="error",
            reason_text=ERROR,
        )
        try:
            await emit(failed)
        except Exception as publish_error:  # the core's timeout fails the inspection instead
            logger.warning(
                "Could not publish the failure for tenant %s (%s)",
                tenant_id,
                type(publish_error).__name__,
                extra={"tenant_id": str(tenant_id)},
            )
        return "dead-letter"


async def start(
    url: str, worker: Worker, delay: Callable[[int], float] = retry_delay
) -> AbstractRobustConnection:
    """Connect, declare the topology and start consuming ai.inspections. The caller closes the
    connection."""

    async def handler(body: bytes, delivery_count: int, publish: Publish) -> Action:
        return await handle(body, delivery_count, worker, publish)

    consumed: tuple[type[Payload], ...] = (InspectionRequested,)
    return await consume(url, QUEUE, consumed, handler, delay)
