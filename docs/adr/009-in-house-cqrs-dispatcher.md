# 009. Use an in-house CQRS dispatcher

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

Every use case in the .NET core is a command or a query with exactly one handler. All handlers need the same steps around them: logging, input validation and, later, a transaction that sets the tenant for RLS. MediatR is the usual library for this, but since 2025 it has required a commercial license for many uses. Normetry is a public portfolio project, and a small dispatcher shows how the pattern works. Only the .NET core needs a dispatcher: the Python service has no command or query pipeline.

## Decision

We write a small dispatcher in `Normetry.BuildingBlocks/Cqrs`.

- `ICommand`, `ICommand<TValue>` and `IQuery<TValue>`, one handler interface for each, and `IDispatcher` with `SendAsync` for commands and `QueryAsync` for queries. Every request returns a `Result` or `Result<TValue>` (`Normetry.BuildingBlocks/Results`). Expected failures (validation, not found, conflict, forbidden) are values, and exceptions are kept for unexpected ones. The API maps a failed result to ProblemDetails.
- `IPipelineBehavior<TRequest, TResult>` wraps the handler. Behaviors run in registration order and can short-circuit. Two ship with the dispatcher: logging (request type and elapsed time, never the payload) and validation with FluentValidation (Apache-2.0).
- `AddDispatcher(assemblies)` scans once at startup and registers every handler and validator as scoped. On the first dispatch of a request type the dispatcher builds a typed wrapper by reflection and caches it, so later dispatches of that type use no reflection.

The dispatcher lives in BuildingBlocks because only .NET uses it. `src/shared/` is reserved for code both backends consume, such as the internal HTTP contract and the queue message contracts. BuildingBlocks still has no dependency on any Normetry project, EF Core or ASP.NET Core. Its only packages are FluentValidation and the Microsoft.Extensions dependency injection and logging abstractions.

## Consequences

- We own the pipeline and its tests. It is a few hundred lines, small enough to read and explain in full.
- No ecosystem extensions: notifications, streaming requests and source-generated dispatch are not included. If a feature needs one, we add it with that feature.
- The transaction behavior (Infrastructure, with the tenants work) goes into the same pipeline after validation, so invalid requests never open a transaction.
- We would revisit this if the dispatcher started growing into a framework, for example if in-process notifications or measured dispatch overhead became a real need.

## Alternatives considered

- **MediatR** — current versions need a commercial license for many uses. The older Apache-2.0 versions still work but no longer get fixes.
- **Wolverine** — it is a messaging framework as well as a mediator, with its own outbox and conventions. We already have an outbox and a queue, so it would overlap with them.
- **Plain handler injection, no dispatcher** — endpoints inject handlers directly. It is the simplest option, but each endpoint would have to wire logging and validation itself, and there would be no single place to add the transaction.
