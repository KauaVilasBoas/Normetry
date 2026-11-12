# src/core — .NET 10 core

## Projects

- `Normetry.Domain` — entities, value objects, domain events. References nothing else: no EF Core, ASP.NET Core or dispatcher.
- `Normetry.Application` — commands, queries, handlers and validators, one folder per bounded context. Depends on Domain and abstractions.
- `Normetry.Infrastructure` — EF Core (Npgsql), outbox, messaging, blob storage; implements Application abstractions.
- `Normetry.Api` — composition root: endpoints, OIDC/JWT auth, YARP `/ai/*`, SignalR hubs, rate limiting.
- `Normetry.AppHost` — Aspire orchestration of every service and container.

Layer dependencies are enforced by NetArchTest. When a rule fails, fix the dependency, not the rule.

Bounded contexts: Tenancy & Identity, Establishments, Documents, Regulations (read-only here), Assessments, Usage & Budget, Notifications.

## Persistence

- Schema `core` only, through EF Core migrations. Never touch `ai.*`.
- Tenant-owned tables follow the `rls-table` skill. The EF Core interceptor sets the tenant per transaction; handlers never set it.
- The app connects as `app_core` (no `BYPASSRLS`, not the owner). Migrations run as a separate owner role, applied at startup in development only, never during a request.
- A state change and its outbox message are written in the same transaction. Consumers are idempotent.

## API

- Commands and queries follow the `cqrs-slice` skill. Errors are ProblemDetails.
- Long-running work returns `202 Accepted`; progress goes out through SignalR.
- YARP strips any client-sent tenant or internal headers, then adds the internal token and tenant headers derived from the validated JWT.
- Rate limiting and the per-tenant token budget live here, not in Python.

## Tests

- Domain: unit tests.
- Handlers and persistence: integration tests with Testcontainers PostgreSQL, RLS included.
- Architecture: NetArchTest rules.

## Configuration

- Secrets through `dotnet user-secrets`, Aspire parameters or environment variables. `appsettings*.json` holds no secrets.
- Formatter: CSharpier as a local dotnet tool; the hook runs it on every edited `.cs` file once installed.
