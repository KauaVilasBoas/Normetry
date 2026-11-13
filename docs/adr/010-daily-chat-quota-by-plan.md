# 010. Enforce a daily chat quota by plan, counted in PostgreSQL

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

Every chat message calls a paid model. The public demo shares one Anthropic account with a monthly cap of about US$ 5, so one demo user must not be able to use it up, while real clients need a higher limit or none. ADR 004 puts rate limiting in the core, in front of the AI service. The API may run as more than one instance, and a deploy restarts it. There is no billing or admin interface yet.

## Decision

- Plans and their limits are configuration: `Chat:Plans:{plan}:DailyMessagesPerUser`, where unset means no limit (`demo` 20, `standard` 200, `unlimited` none). `Chat:TenantPlans:{tenantId}` maps a tenant to a plan, and every other tenant gets `Chat:DefaultPlan` (`demo`). The API refuses to start on an unknown plan name, a tenant key that is not a UUID or a limit below 1. Changing a limit is a configuration change and a restart.
- Counts live in `core.chat_message_counts`, one row per tenant, user (`sub`) and UTC day, under RLS like every tenant table (ADR 003). A single `INSERT ... ON CONFLICT DO UPDATE ... WHERE count < limit` adds a message only below the limit, so concurrent requests never lose a count or pass the limit.
- Only `POST /ai/establishments/{id}/conversations/{conversationId}/messages` counts, because it is the only route that generates an answer. The gateway counts it and commits the count before forwarding, so an answer the AI service then fails on still counts. Over the limit the core answers 429 with `Retry-After` until the next UTC midnight and forwards nothing. Counted responses carry `RateLimit-Limit`, `RateLimit-Remaining` and `RateLimit-Reset`, and `GET /chat/quota` shows what is left before sending.

## Consequences

- Counts survive restarts and agree across API instances, and RLS isolates them by tenant like every other table.
- Each chat message costs one more database write before it is forwarded.
- A new route that calls a model must be added to the count in the same pull request (`src/shared/contracts/internal-http.md`), or it bypasses the quota.
- A failed answer still uses up one of the user's messages.
- Nothing deletes old count rows yet. A cleanup job is a known follow-up, and it cannot run as `app_core`, which has no DELETE on the table.
- When billing arrives, plans move from configuration to the database.

## Alternatives considered

- **ASP.NET Core rate limiter (in memory)** — each instance keeps its own counters and loses them on restart, so with two instances or a deploy a user gets more than the limit. Its windows are fixed spans of time, not UTC days, and it keeps no count that `GET /chat/quota` could read.
- **Redis counter** — shared and fast, but one more service to run and secure for a few writes per user per day, and it has no RLS. PostgreSQL is already there.
- **Counting in the AI service** — the request must be stopped before it reaches the AI service, and the AI service writes only schema `ai` (ADR 002).
- **Plans in the database now** — needs an admin interface or billing that does not exist yet; configuration is enough until then.
