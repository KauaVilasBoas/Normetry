---
name: cqrs-slice
description: How to add a use case (command or query) to the Normetry .NET core as a vertical slice across Domain, Application, Infrastructure and Api, with its tests. Use when implementing a new endpoint, command, query or handler in src/core.
---

# CQRS slice in src/core

## First: copy the house pattern

Find the most recent existing slice (search `src/core` for `Handler`) and mirror its file layout, naming, registration and test style. This skill states the rules; the existing code is the template.

## Dispatcher

Normetry uses its own dispatcher in `Normetry.BuildingBlocks/Cqrs` ([ADR 009](../../../docs/adr/009-in-house-cqrs-dispatcher.md)). Do not add MediatR or another mediator.

- A command implements `ICommand` (returns `Result`) or `ICommand<TValue>` (returns `Result<TValue>`); a query implements `IQuery<TValue>`. The handler implements `ICommandHandler<TCommand>`, `ICommandHandler<TCommand, TValue>` or `IQueryHandler<TQuery, TValue>`.
- Registration is automatic: `AddDispatcher(...)` in `Program.cs` scans the Application assembly for handlers and FluentValidation validators. Do not register them by hand.
- Endpoints inject `IDispatcher` and call `SendAsync` (commands) or `QueryAsync` (queries), passing the request's `CancellationToken`.
- Expected failures are values: return a failed `Result` with an `Error` (`Validation`, `NotFound`, `Conflict`, `Forbidden`, `Unexpected`). Throw only for unexpected failures.
- Input validation is an `AbstractValidator<TRequest>` next to the command. The validation behavior runs it and returns a 400 without calling the handler.
- The endpoint maps a failure with `result.ToProblemDetails()` (`Normetry.Api`), which picks 400/404/409/403/500 and hides the message on a 500.
- Cross-cutting steps are `IPipelineBehavior<TRequest, TResult>`. Behaviors run in registration order: logging, then validation (both registered by `AddDispatcher`), then anything registered after it. A behavior must never log the request payload.

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
