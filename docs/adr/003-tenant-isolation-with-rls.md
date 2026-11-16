# 003. Isolate tenants with PostgreSQL Row-Level Security

- **Status:** Accepted (amended 2026-11-16: only a dedicated `core_dispatcher` role reads the list of tenants)
- **Date:** 2026-11-11

## Context

Tenants share tables: every customer's documents, findings and usage sit side by side, and `ai.chunks` mixes the public regulation corpus with tenant documents. The security target is zero cross-tenant leaks. Two services in two languages query the same database, and a filter in application code works only if every query remembers its `WHERE tenant_id = ...`. Connections are pooled, so any per-connection state can leak into the next request.

## Decision

Every tenant-owned table has RLS enabled and forced (`FORCE ROW LEVEL SECURITY`, so it applies to the table owner too). Its policies compare `tenant_id` with `current_setting('app.tenant_id', true)`.

- The tenant is set per transaction with `set_config('app.tenant_id', id, true)`. The last argument makes the setting transaction-local, so a pooled connection never carries it into the next request. In .NET an EF Core interceptor sets it; in Python a unit-of-work helper does, and workers take the tenant from the queue message. Handlers never set it themselves. The id comes from the validated JWT (ADR 004).
- With no tenant set, the policies show only the public corpus and reject writes, so a missing setup fails closed.
- App roles have no `BYPASSRLS`, are not superusers and do not own the tables; migrations run as a separate owner role. Only `corpus_ingestor` can write rows with a null `tenant_id`, so no tenant can poison the public corpus.
- Amendment: the background services (outbox dispatcher, timeout sweeper) need one query without a tenant, the list of tenants to visit in `core.outbox_tenants`. It runs as `core_dispatcher`, a login role whose only table privilege is `SELECT` on that table. `app_core` can only insert its own tenant's row and cannot read the list, so an injection on a request path cannot enumerate tenants. Everything per tenant still runs as `app_core` with the tenant set per transaction.

## Consequences

- A buggy or hand-written SQL query still cannot return another tenant's rows.
- Every transaction has to set the tenant first, and the tenant must never be set at session level, or a pooled connection would carry it over.
- Policies add a predicate to every query, so indexes for tenant-scoped lookups lead with `tenant_id`.
- Each tenant-owned table needs an isolation test against a real PostgreSQL (Testcontainers, connected as the app role): tenant B cannot read, update or delete tenant A's rows, and with no tenant set, no tenant rows are visible and writes fail. The `rls-table` skill holds the recipe.
- The core runs with two credentials, `app_core` and `core_dispatcher`. The startup role check refuses either one if it is a superuser, bypasses RLS or owns anything in `core`, and a new table never reaches `core_dispatcher`, because the bootstrap gives it no default privileges.
- We would revisit this if a customer required physical separation of its data.

## Alternatives considered

- **Tenant filter in application code only** — EF Core global query filters would cover .NET, Python would need its own, and one missed filter or raw query leaks data.
- **Database or schema per tenant** — stronger isolation, but migrations, connection pools and operations grow with the number of tenants, and most tenants are small.
