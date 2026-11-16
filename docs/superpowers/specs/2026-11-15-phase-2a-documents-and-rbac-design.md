# Phase 2a design: tenant documents and permission-based access

Status: under review (2026-11-16). Phase 2 (simulated inspection) is split into separate designs:

1. **This one:** permission-based access (RBAC) and the asynchronous document pipeline.
2. Synthetic establishments with planted non-conformities, used for the demo and the inspection evals.
3. The inspection agent, findings and human review.
4. Chat answers that also search the customer's documents.

Items 2 to 4 get their own designs. They are listed here only so that this design leaves room for them.

## Intent

Before an inspection can run, its documents have to exist. Today the core has no documents, no outbox, no queue, no blob storage and no SignalR. Authorization is a fixed set of three Keycloak realm roles. This design adds:

- **Documents.** An Owner or Manager uploads the establishment's documents: SOPs, licenses, temperature logs, lab reports. Each document can have versions. Ingestion is asynchronous: upload, then an outbox event, a queue, a Python worker, `ai.chunks`, a result event and a live status in the web app. Phase 2's inspection reads these chunks.
- **Permissions.** Access is decided by permissions stored in the core database. A tenant manages its own profiles from a "Profiles" tab. Each profile holds N permissions, and each user holds N profiles. Every controller action declares the permission it needs with an attribute.

The public repository should show the mechanics the README promises: a transactional outbox, at-least-once delivery with idempotent consumers, tenant isolation across the queue and the blob store, and progress over SignalR.

## Goals

- Upload PDF (with a text layer), DOCX, TXT and CSV files of up to 10 MB and 100 pages. A file that fails these rules, or a PDF without text, ends in `Failed` with a reason the user can read.
- Document status runs `Uploaded → Processing → Indexed | Failed`, and the web app shows each change live.
- Uploading a new version of a document keeps its history. Once the new version is indexed, its chunks replace the previous version's chunks.
- A tenant's profiles, each profile's permissions and each user's profiles can be managed in the app, and every endpoint checks a permission.
- The demo establishments start with their documents already ingested.
- Nothing crosses tenants: not in the queue, the blob store, `ai.chunks` or SignalR groups.

## Non-goals

- OCR, scans and photos. A PDF without a text layer fails with "no text layer".
- Chat over tenant documents (design 4), the inspection itself (design 3) and authoring the synthetic documents (design 2).
- Inviting or creating users from the app. Users are created in Keycloak. The core learns about them at their first sign-in.
- Corporate SSO, billing and per-feature budgets.

## Anti-goals

- A tenant could see, search or overwrite another tenant's file, chunk or status. That is a failure even if every feature works.
- An admin could lock everyone out of profile management.
- A message could be lost between the database commit and the queue.
- A document could stay in `Processing` forever without any sign of it.

## Decisions

### Permission-based access (RBAC)

- **Model**, all in schema `core`:
  - `permissions`: the catalog. It is fixed in code and inserted by a data migration. It is global reference data with no tenant.
  - `profiles`: per tenant, with a name and an `is_system` flag.
  - `profile_permissions`: the N:N link between profiles and permissions.
  - `users`: per tenant. A user is identified by the token's `sub`. The table keeps a display name and an email, taken from the token's claims, so the Profiles tab can show who is who.
  - `user_profiles`: the N:N link between users and profiles.
  - Every table except `permissions` is tenant-owned, with RLS (`rls-table` skill) and cross-tenant isolation tests.
- **Initial catalog.** Names are `area.action`:
  - `establishments.read`, `establishments.manage`
  - `documents.read`, `documents.upload` (which also covers new versions), `documents.delete`
  - `chat.ask`
  - `profiles.read`, `profiles.manage` (which also covers assigning profiles to users)
  - The inspection permissions arrive with design 3.
- **Default profiles.** Every tenant has three system profiles. None of them can be deleted.
  - Owner grants every permission implicitly, through a `grants_all` flag rather than permission rows. Permissions added to the catalog later, such as design 3's, reach it with no data change. It cannot be edited.
  - Manager: every permission except `profiles.manage`. Stored as rows, and the tenant can edit them.
  - Auditor: `establishments.read`, `documents.read`, `chat.ask` and `profiles.read`. Also rows the tenant can edit.

  A tenant can add profiles of its own. A permission added to the catalog reaches only Owner, and the tenant grants it to the other profiles.

  System profiles are created at runtime, never by a migration. Migrations run as `core_owner`, which RLS keeps out of tenant tables, and the demo tenants do not exist yet when migrations run. So the code path that creates a tenant creates its system profiles in the same transaction. A startup step creates them for any tenant that lacks them, which covers the demo tenants that `DemoTenantSeed` creates.
- **Users.** A user row is created on the first authenticated request, from the token's `sub` and `tenant_id`. Users with no profile get 403 from everything except `/me`, and the web app tells them to ask an administrator.
- **First Owner** (decided 2026-11-16): every tenant always has an admin, a user who holds the Owner profile. The admin is created together with the tenant, by the same code path, so no tenant ever exists without one. A migration cannot do this: tenants do not exist yet when migrations run, and RLS keeps `core_owner` out of tenant tables.
  - Today tenants come only from the dev seed. `DemoTenantSeed` creates each demo tenant with its admin user row and Owner profile. It also creates the other demo users with their profiles: Manager for `bistro.manager`, Auditor for `bakery.auditor`.
  - The users' `sub` must not change between realm imports, so the demo users get fixed ids in `deploy/keycloak/normetry-realm.json`.
  - A new demo user, `bakery.owner`, gives Demo Bakery its admin.
  - A platform admin above the tenants was considered and left for when real customer onboarding exists (phase 3). It would break the "tenant comes from the JWT" invariant and needs its own ADR.
- **Lockout guard.** Owner always holds `profiles.manage`, so the guard is one rule: a tenant can never lose its last Owner. Removing the Owner profile from the last user who holds it is refused with a problem response.
- **Checks.** API endpoints move from minimal APIs to MVC controllers (`[ApiController]`). Every action carries `[HasPermission(Permissions.Documents.Upload)]`, or `[AllowAnonymous]` where one is needed, which today is nowhere. A custom authorization policy provider turns the attribute into a policy. Its handler loads the user's permissions under the tenant transaction.
  - The `/ai/*` chat route has no controller method, so it requires `chat.ask` through the YARP route's authorization policy.
  - The SignalR hub uses the same attribute.
  - The fallback policy stays "authenticated with a tenant", so an action without an attribute is never anonymous. A test walks every controller action and fails if one has no `HasPermission` and no `AllowAnonymous`.
- **Keycloak** keeps authentication and the `tenant_id` attribute. Realm roles no longer authorize anything. `NormetryRoles`, the role policies, the role-to-claim mapping and the realm roles themselves are all removed, because the demo users get their profiles from the seed.
- **`/me`** returns the user's permissions, so the web app can hide what the user cannot do. The server still checks every request.
- **ADR 012** records the move from realm roles to permissions in the core database.

### Documents (core, Documents context)

- **Model.** `documents` holds the establishment, title, type (SOP, license, log, report, other), current version and status of the current version. `document_versions` holds the version number, original file name, content type, size, SHA-256, blob path, status, failure reason, uploader and timestamps. Both tables are tenant-owned, with RLS.
- **Upload** is `POST` with multipart, and returns `202 Accepted` with the document and version ids:
  - The API checks the size (10 MB), the extension and content type against the allowed formats, and the magic bytes.
  - It writes the file to the blob store, then writes the version as `Uploaded` together with an outbox message, in one transaction.
  - A new version is a `POST` to the same document's versions.
  - Checking the page count needs a parser, so the worker does it and fails the version if it is over 100 pages.
- **Limit:** 50 documents per establishment. This is my call, as a guard for the public demo; see "Left to the builder".
- **Blob path:** `tenants/{tenantId}/establishments/{establishmentId}/documents/{documentId}/versions/{versionId}/{file}`.
  - Locally the store is Azurite, run by the AppHost. In production it is Azure Blob Storage. Both use the same SDK. This replaces the MinIO mention in the README.
  - The worker refuses a message whose blob path does not start with its own `tenants/{tenantId}/`.
- **Status:** the version moves to `Processing` on `DocumentIngestionStarted`, to `Indexed` on `DocumentIndexed` and to `Failed` on `DocumentIngestionFailed`.
  - A version stuck in `Processing` past a timeout (my call: 15 minutes) is marked `Failed` with the reason "timed out", by the same background service that dispatches the outbox.
  - A late `DocumentIndexed` still moves a timed-out version to `Indexed`, because its chunks are live and the status must say so.
  - `Uploaded` has no timeout. The queue is durable and the outbox retries, so the document waits for the AI service. The web app shows it as "waiting".
- **One version in flight.** Uploading a new version is refused with 409 while the document's newest version is `Uploaded` or `Processing`.
  - The document's current version is its newest `Indexed` version, and that is what the inspection reads. The newest version's status is shown next to it.
  - A failed new version leaves the previous version current and searchable.
- **Versions in the AI schema.**
  - The chunks of a tenant document carry `source_type = 'tenant_document'`, `source_id` = the document id and `source_version` = the version number. The column is `int`, and the message carries the number. `ai.chunks` already has these columns and the tenant insert and delete policies.
  - A new tenant-owned table, `ai.tenant_documents`, holds the document id, establishment id, current indexed version number and a deleted marker. It has RLS and is written only by the consumer from messages. Designs 3 and 4 join it to search one establishment's documents. It also keeps late or out-of-order messages from undoing newer state.
  - In one transaction, the consumer locks the document's row and checks it. If the document is deleted, or the version is older than the indexed one, the consumer drops the message's work and acknowledges it.
  - Otherwise it inserts the version's chunks, deletes the chunks of every other version of the document, and records the version as current.
  - Findings (design 3) keep a copy of the passage they cite, as chat answers do. Old inspections therefore survive a new version.
- **Delete:** `documents.delete` soft-deletes the document. The `DocumentDeleted` event marks the `ai.tenant_documents` row deleted and removes its chunks, so an ingestion still in flight finds the marker and stores nothing. Blobs stay, and retention is out of scope.

### Messaging

- **Broker:** RabbitMQ, run by the AppHost. Azure Service Bus in production is phase 3.
- **Outbox.** It is hand-written, like the dispatcher of ADR 009:
  - An `outbox_messages` table in `core`, written in the same transaction as the state change.
  - A background dispatcher publishes pending rows with `RabbitMQ.Client` and publisher confirms, then marks them sent. Delivery is at least once.
  - The W3C `traceparent` goes in the message headers.
- **Inbox.** The core consumes the result events with an `inbox_messages` table keyed by message id. A redelivered event changes nothing. Each consumer builds its scoped `ITenantContext` from the message and uses the tenant-scoped database, as `src/core/CLAUDE.md` already requires.
- **Contracts (#16).** Versioned JSON Schemas in `src/shared/contracts/messages/`, validated by contract tests on both sides:
  - core to ai: `DocumentUploaded` (tenant, establishment, document, version id and number, blob path, content type, size, SHA-256) and `DocumentDeleted`.
  - ai to core: `DocumentIngestionStarted`, `DocumentIndexed` (chunk count, page count, language) and `DocumentIngestionFailed` (reason code and text).
- **Python side.** The consumer is an `aio-pika` task inside the AI service process. It shares the BGE-M3 embedder the service already loads. This is my call, because the developer machine already runs out of memory with the AppHost. The ceiling: one process does both chat and ingestion. Split the worker out when ingestion load slows chat.
  - The consumer sets the tenant per transaction from the message, as `app_ai`.
  - It is idempotent per version: a redelivered `DocumentUploaded` for an indexed version is acknowledged and dropped.
  - A failure the worker cannot recover from publishes `DocumentIngestionFailed`. Anything else is requeued with a retry limit, then sent to a dead-letter queue.

### Ingestion (Python)

- **Parsers:**
  - PDF: PDFium (`pypdfium2`, already a dependency) reads the text layer. If a PDF yields almost no text per page, it fails with "no text layer".
  - DOCX: a small library (`python-docx`). It keeps headings for `section_path`.
  - TXT: read as UTF-8, with a Latin-1 fallback.
  - CSV: rows are grouped into chunks with their header repeated, so a temperature log stays readable.
  - Docling is not used (`src/ai/CLAUDE.md`, "Corpus parsing").
- **Chunking:** by heading where the format gives one, by paragraph groups otherwise, within the size limits of the corpus chunker.
- **Language:** `detect_language` per document.
- **Indexing:** the same embedding and `tsvector` as the corpus. The searches that read these chunks come with designs 3 and 4.

### Real-time status

- A SignalR hub in the core (`/hubs/documents`) puts each connection in a group per tenant and establishment.
  - Joining a group needs `documents.read` and an establishment of the caller's tenant.
  - Status changes are pushed after the inbox transaction commits.
- The web app reconnects and refetches the list after a reconnect, so a missed push only delays the update.

### Web app

- **Documents screen:**
  - Upload by picker or drag and drop.
  - A list with live status badges: Uploaded, Processing, Indexed, Failed with its reason.
  - A document detail page with its versions and an "upload new version" action.
- **Profiles tab, under settings:**
  - The tenant's profiles, with a grid of the permission catalog grouped by area to edit them. System profiles can be edited but not deleted.
  - The tenant's users, with their profiles to assign and unassign.
  - The lockout guard's refusal is shown inline.
- Actions the user lacks the permission for are hidden, using the permissions from `/me`.
- Both screens get a `ux-ui-designer` spec before they are built and a review after.

### Development seed

The dev seed uploads the demo establishments' documents through the real pipeline after the AppHost starts, from `evals/establishments/<establishment>/documents/`. Until design 2 writes the synthetic documents, a few short placeholder documents cover the formats. The seed is idempotent: it skips a document whose current version has the same SHA-256.

## Constraints

- The invariants in the root `CLAUDE.md`:
  - .NET writes only `core` and Python writes only `ai`.
  - RLS with the tenant set per transaction.
  - The tenant comes from the JWT on HTTP and from the message on the queue.
  - The AI service has no public ingress.
  - Nothing secret in the repository.
- Every new tenant-owned table follows the `rls-table` skill, with its isolation tests.
- Changes to persistence, auth, YARP, queue consumers, blob paths and SignalR groups get a `tenant-isolation-reviewer` review.
- Every frontend change gets a `ux-ui-designer` spec and review.
- The developer machine has 16 GB, and the AppHost already uses most of it. New resources (RabbitMQ, Azurite) must stay small.
- The workflow is the root `CLAUDE.md`'s: one issue per feature, atomic Conventional Commits, rebase merges.

## Approaches

The how-to choices were made in conversation (2026-11-15):

- **Where the permission attribute goes.**
  - Picked: move to MVC controllers with `[HasPermission]` on each action. It is what the author asked for, and it is the usual .NET pattern. Only 3 endpoint files exist today.
  - Not picked: keep minimal APIs and put the attribute on handler methods. It works the same with less churn, but there is no controller.
- **Messaging.**
  - Picked: a hand-written outbox and inbox with `RabbitMQ.Client` and `aio-pika`. It is consistent with ADR 009, and every piece stays visible.
  - Not picked: Wolverine, a big dependency that hides the mechanics.
  - Not picked: MassTransit, because v9 is commercial.
- **Blob storage.**
  - Picked: Azurite locally and Azure Blob in production, with one SDK in both places. This is my call.
  - Not picked: MinIO with an S3 SDK locally, which needs a second SDK or a compatibility layer for Azure.

## Left to the builder

- Table and column names, indexes, and the exact problem codes.
- How the policy provider caches permissions: per request at first. A cache needs invalidation when profiles change.
- Chunk size limits for tenant documents, and how CSV rows are grouped.
- Retry counts, backoff, the dead-letter queue name and the outbox polling interval.
- The text-per-page threshold for "no text layer".
- The 50-documents limit and the 15-minute processing timeout, as values in configuration.
- The order inside each issue.

## Records to update

- ADR 012 (new): authorization by permissions stored in the core database.
- README:
  - Architecture: Azurite instead of MinIO, and Anthropic instead of OpenRouter (phase 1 calls Anthropic directly).
  - The Tenancy & Identity context: profiles and permissions instead of fixed roles.
  - The document ingestion flow: the parsers instead of Docling and OCR.
- `src/core/CLAUDE.md` and `src/ai/CLAUDE.md`: controllers and `[HasPermission]`, the outbox and inbox, the consumer.
- `deploy/keycloak/README.md`: realm roles no longer authorize, and the demo users' profiles come from the dev seed.
