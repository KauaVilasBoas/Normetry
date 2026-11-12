# 005. Use a transactional outbox for long-running work and SSE for chat

- **Status:** Accepted
- **Date:** 2026-11-11

## Context

Document ingestion (parse, OCR, chunk, embed) and a simulated inspection (batches of LLM judgments) can take minutes. An HTTP request held open that long hits timeouts, and a crash in the middle loses the work. When a document is uploaded or an inspection is requested, the core must tell the AI service, and that event cannot be lost if the process dies right after the database commit. A chat answer takes seconds, and the user should see it while it is generated.

## Decision

- Long-running work is asynchronous. The endpoint writes the state change (for example `Document = Uploaded`) and an outbox message in one transaction, then returns `202 Accepted`. An outbox dispatcher publishes pending messages to the queue.
- The queue is RabbitMQ locally and Azure Service Bus in production. Python workers consume the messages and publish result events such as `DocumentIndexed`; the core consumes those and writes the result to `core`.
- The core pushes progress to the web app with SignalR.
- Chat streams over SSE from FastAPI through YARP (ADR 004) to the browser.

## Consequences

- No event is lost between the commit and the queue. If the process dies after the commit, the dispatcher still finds the message in the outbox and publishes it.
- Delivery is at least once, so every consumer must be idempotent. Ingestion identifies each chunk by a content hash, and a new document version replaces the previous chunks in the same transaction.
- The UI is eventually consistent: a document is not `Indexed` until its result event arrives and SignalR pushes the change.
- Each message carries its tenant id, and workers set the tenant per transaction from it (ADR 003). The W3C `traceparent` travels in message headers, so one trace covers the whole flow.
- More to run: the outbox table, its dispatcher and a broker.

## Alternatives considered

- **Synchronous HTTP from the core to Python** — long documents and inspections hit request timeouts, and a crash loses the work in progress.
- **Publish to the queue right after the commit** — a dual write. If the process dies between the commit and the publish, the event is gone and the document never leaves `Uploaded`.
- **Chat through the queue and SignalR** — it adds a broker hop and a SignalR hop to a request that finishes in seconds, and the user can retry a dropped chat stream.
