---
name: cqrs-slice
description: How to add a use case (command or query) to the Normetry .NET core as a vertical slice across Domain, Application, Infrastructure and Api, with its tests. Use when implementing a new endpoint, command, query or handler in src/core.
---

# CQRS slice in src/core

## First: copy the house pattern

Find the most recent existing slice (search `src/core` for `Handler`) and mirror its file layout, naming, registration and test style. This skill states the rules; the existing code is the template.

If no slice exists yet, this is the first one. Ask the user which dispatcher to use (a small in-house dispatcher, or MediatR, which has required a commercial license since 2025) and record the answer with the `adr` skill before writing code.

## Placement

| Piece | Project | Notes |
|---|---|---|
| Entity, value object, domain event | `Normetry.Domain` | Invariants live here. No EF Core, ASP.NET Core or dispatcher references. |
| Command / query, handler, validator | `Normetry.Application` | One folder per bounded context and use case, e.g. `Establishments/CreateEstablishment/`. Handlers depend on abstractions. |
| EF Core configuration, repositories, outbox writer | `Normetry.Infrastructure` | Schema `core` only. |
| Endpoint | `Normetry.Api` | Thin: bind, authorize, dispatch, map the result to HTTP. |

## Rules

- Commands change state and return at most an id or a small result. Queries never change state.
- Validate input at the API boundary; enforce invariants in the domain.
- A state change other services must learn about writes an outbox message in the same transaction.
- Long-running work (ingestion, inspection) returns `202 Accepted` and continues asynchronously. Never block a request on the AI service.
- Errors map to ProblemDetails. Never leak exception details.
- The tenant comes from the authenticated user context, never from the request body.

## Tests

- Domain invariants: unit tests.
- Handler + persistence: integration test with Testcontainers PostgreSQL. Tenant-owned data needs the isolation test from the `rls-table` skill.
- A new project reference or layer crossing: the NetArchTest suite must still pass. Fix the dependency, not the rule.
