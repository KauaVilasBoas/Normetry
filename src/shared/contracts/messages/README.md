# Queue message contracts: core and AI service

The core and the AI service talk over RabbitMQ only through the messages in this folder. Both sides implement this file and the schemas next to it; a change here changes both sides in the same pull request. Design: `docs/superpowers/specs/2026-11-15-phase-2a-documents-and-rbac-design.md`, "Messaging".

| File | What |
|---|---|
| `envelope.v1.schema.json` | the body every message shares |
| `<message>.v<N>.schema.json` | one message version: its `type`, `version` and `payload` |
| `fixtures/<message>.v<N>.valid.json`, `.invalid.json` | one valid and one invalid body per message (the invalid one breaks exactly one rule) |
| `published.json` | the required fields of every published version, frozen (see "Versions") |

The schemas are JSON Schema draft 2020-12 and use only `$ref`, `allOf`, `not`, `const`, `enum`, `type`, `pattern`, length and range keywords, so any 2020-12 validator reads them (Python `jsonschema`, .NET `JsonSchema.Net`). Ids and timestamps are checked with `pattern`, never with `format`, which validators treat as an annotation by default. Each schema's `$id` is under `https://contracts.normetry.example/messages/` and refers to the envelope by relative `$ref`: register every schema file in the validator before validating.

## Envelope

The body is UTF-8 JSON, camelCase:

```json
{
  "messageId": "0b8f6c3e-2d1a-4e5f-9a8b-7c6d5e4f3a21",
  "type": "DocumentUploaded",
  "version": 1,
  "occurredAt": "2026-11-16T12:00:00Z",
  "tenantId": "3f0c2a9e-6b1d-4c8e-9a57-2d4e8b1c7f60",
  "payload": { "...": "..." }
}
```

- `messageId`: unique per message. Consumers deduplicate on it (the core's inbox is keyed by it).
- `type` and `version`: the message and its version; they pick the schema.
- `occurredAt`: RFC 3339 with an offset (`Z` or `+00:00`).
- `tenantId`: the tenant, a non-nil UUID. **The only source of the tenant for a consumer.** A consumer sets the tenant of its database transaction from it and from nothing else.
- Every UUID is lower-case 8-4-4-4-12, as `Guid.ToString()` and Python's `str(UUID)` write it.

The body is authoritative. The AMQP properties and headers below repeat or add to it for the broker and for tracing; a consumer never reads the tenant, type or version from them.

| AMQP property or header | Value |
|---|---|
| `content_type` | `application/json` |
| `delivery_mode` | `2` (persistent) |
| `message_id` | the body's `messageId` |
| `type` | the body's `type` |
| header `traceparent` | W3C trace context of the publishing span. Publishers set it whenever they have a trace; consumers continue the trace from it and accept its absence. |
| header `tracestate` | optional, W3C, passed on when present |

## Messages

| Message | From → to | Routing key | Queue |
|---|---|---|---|
| `DocumentUploaded` v1 | core → ai | `document.uploaded.v1` | `ai.documents` |
| `DocumentDeleted` v1 | core → ai | `document.deleted.v1` | `ai.documents` |
| `DocumentIngestionStarted` v1 | ai → core | `document.ingestion-started.v1` | `core.document-status` |
| `DocumentIndexed` v1 | ai → core | `document.indexed.v1` | `core.document-status` |
| `DocumentIngestionFailed` v1 | ai → core | `document.ingestion-failed.v1` | `core.document-status` |

- `DocumentUploaded`: establishment, document, version id and number, `blobPath`, `contentType` (no parameters), `size` in bytes and `sha256` (lower-case hex). The blob path is `tenants/{tenantId}/establishments/{establishmentId}/documents/{documentId}/versions/{versionId}/{file}`; the AI service refuses one outside `tenants/{tenantId}/` of the envelope's tenant.
- `DocumentDeleted`: establishment and document. The AI service keeps a deleted marker, so an ingestion in flight or a late `DocumentUploaded` stores nothing.
- `DocumentIngestionStarted`, `DocumentIndexed`, `DocumentIngestionFailed`: establishment, document, version id and number. `DocumentIndexed` adds `chunkCount`, `pageCount` (null for DOCX, TXT and CSV) and `language` (`en` or `pt`); `DocumentIngestionFailed` adds `reasonCode` and `reasonText`.
- The AI service gives its events a message id derived from the version (UUIDv5 of type and version id), so re-publishing one after a redelivery is deduplicated by the core's inbox.

### Failure reasons

| `reasonCode` | When |
|---|---|
| `unsupported_format` | `contentType` is not PDF, DOCX, TXT or CSV |
| `no_text_layer` | a PDF with under 50 non-whitespace characters per page on average (scans; there is no OCR) |
| `too_many_pages` | a PDF over 100 pages |
| `too_large` | over 10 MiB (10,485,760 bytes) by `size` or by the stored blob, or a DOCX that unpacks to over 50 MiB |
| `blob_missing` | no blob at `blobPath` |
| `blob_path_outside_tenant` | `blobPath` is not under `tenants/{tenantId}/` |
| `parse_error` | the file cannot be read as its format, or holds no text |
| `internal` | anything else: the stored file's SHA-256 differs from the message's, or the last retry failed |

`reasonText` is a short English sentence for the user and never quotes the document. The core may show its own text for the code. The core's own "timed out" is not a code here: the AI service never sends it.

## Topology

| Name | Kind | Arguments |
|---|---|---|
| `normetry.events` | topic exchange, durable | every message is published here with its routing key |
| `normetry.dead-letter` | direct exchange, durable | |
| `ai.documents` | quorum queue, durable | `x-queue-type: quorum`, `x-dead-letter-exchange: normetry.dead-letter`, `x-dead-letter-routing-key: ai.documents`, `x-delivery-limit: 4`; bound to `normetry.events` with `document.uploaded.v1` and `document.deleted.v1` |
| `ai.documents.dead-letter` | quorum queue, durable | `x-queue-type: quorum`; bound to `normetry.dead-letter` with `ai.documents` |
| `core.document-status` | quorum queue, durable | the same arguments with `core.document-status`; bound with the three ai → core keys |
| `core.document-status.dead-letter` | quorum queue, durable | bound to `normetry.dead-letter` with `core.document-status` |

- Each service declares both exchanges and the queues it consumes, with their bindings and dead-letter queue, at startup. Declarations are idempotent, but their arguments must match this table exactly, or the broker closes the channel with `PRECONDITION_FAILED`.
- Publishers use publisher confirms and the `mandatory` flag. A message the broker cannot route (the consumer has not declared its queue yet) comes back and counts as a failed publish to retry, never as sent.

## Delivery and retries

- Delivery is at least once, and every consumer is idempotent: the core by `messageId` (inbox), the AI service per document version (`ai.tenant_documents`).
- A message that is not valid JSON, names an unknown type or version, or breaks its schema is rejected without requeue, which dead-letters it at once.
- A permanent failure (the reasons above) is an outcome, not an error: the AI service publishes `DocumentIngestionFailed` and acknowledges.
- Anything else is retried: the consumer waits (the AI service 1, 2, 4, then 8 seconds) and rejects the message with requeue. RabbitMQ counts a `basic.reject` in the message's `x-delivery-count` header (a `basic.nack` it does not, since 4.3), as it counts a delivery lost with its connection. With `x-delivery-limit: 4` a message gets five deliveries, then goes to the dead-letter queue. On its last delivery the AI service publishes `DocumentIngestionFailed` with `internal` before dead-lettering, so the version does not wait for the core's timeout.
- Dead-lettered messages stay in their dead-letter queue for an operator. Nothing consumes them.

## Versions

- A published version never changes in a breaking way. A breaking change publishes a new version: a new schema file (`document-uploaded.v2.schema.json`), its fixtures, its entry in `published.json` and a new routing key (`document.uploaded.v2`). The publisher sends the new version once its consumers read it; consumers read both until the old one is drained.
- Breaking: adding, removing or renaming a required field; making an optional field required; changing a field's type, `const` or `enum` (a new `reasonCode` too, because consumers map the codes); tightening a constraint.
- Not breaking: descriptions, and adding an optional field. Readers are tolerant: they ignore payload fields they do not know, so an old consumer reads a message from a newer producer. Producers write exactly their schema (`additionalProperties: false`), so the contract tests catch a field the schema does not name.
- `published.json` freezes the required fields of every published version (their `type`, `const`, `enum` or `$ref`). The contract tests compare each schema with it, so changing a required field without a new version fails them. Edit an entry only to add a new version.

## Contract tests

- Python (`src/ai/tests/test_contracts.py`): every schema is valid 2020-12; every fixture validates (or, the invalid ones, fails exactly one rule) against its schema and against the Python models; the models' own output validates against the schemas; the required fields match `published.json`.
- .NET: the same checks against the C# message types, with the core's messaging (#84, #85).
