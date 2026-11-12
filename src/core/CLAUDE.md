# src/core — .NET 10 core

## Projects

Solution: `Normetry.slnx` at the repo root. Shared build settings live in the root `Directory.Build.props` (net10.0, nullable, warnings as errors) and package versions in `Directory.Packages.props` (central package management: `PackageReference` items carry no `Version`).

- `Normetry.BuildingBlocks` — shared primitives with no Normetry, EF Core or ASP.NET Core dependency. `Cqrs/` holds the in-house dispatcher (ADR 009): commands, queries, handlers, `IDispatcher`, pipeline behaviors (logging, FluentValidation) and `AddDispatcher(...)`, which scans for handlers and validators. `Results/` holds `Result`, `Result<T>` and `Error` for expected failures; `Normetry.Api` maps a failed result to ProblemDetails with `ToProblemDetails()`.
- `Normetry.Domain` — entities, value objects, domain events. References only BuildingBlocks: no EF Core, ASP.NET Core or dispatcher.
- `Normetry.Application` — commands, queries, handlers and validators, one folder per bounded context. References Domain and BuildingBlocks.
- `Normetry.Infrastructure` — EF Core (Npgsql), outbox, messaging, blob storage; implements Application abstractions. References Application.
- `Normetry.Migrations` (arrives with #12) — EF Core migrations only, in `Schema/` (structure, RLS, grants) and `Data/` (rows every environment needs). A console app the AppHost runs as `core_owner` before the API starts; see "Database migrations" in the root `CLAUDE.md`.
- `Normetry.Api` — composition root: endpoints, OIDC/JWT auth, YARP `/ai/*`, SignalR hubs, rate limiting. References Application, Infrastructure and ServiceDefaults.
- `Normetry.ServiceDefaults` — Aspire service defaults: OpenTelemetry, health checks (`/health`, `/alive`, Development only), service discovery, HTTP resilience.
- `Normetry.AppHost` — Aspire orchestration of every service and container. DCP and the dashboard come from NuGet (`AspireUseCliBundle=false`), so no Aspire CLI install is needed.

Layer dependencies are enforced by NetArchTest (`tests/Normetry.ArchitectureTests`). When a rule fails, fix the dependency, not the rule. A reference pointing up the layers (for example Domain → Infrastructure) is already rejected by MSBuild as a circular reference; the tests catch leaks such as EF Core or ASP.NET Core types in Domain.

Bounded contexts: Tenancy & Identity, Establishments, Documents, Regulations (read-only here), Assessments, Usage & Budget, Notifications.

## Persistence

- Schema `core` only, through EF Core migrations. Never touch `ai.*`.
- Tenant-owned tables follow the `rls-table` skill. The EF Core interceptor sets the tenant per transaction; handlers never set it.
- The app connects as `app_core` (no `BYPASSRLS`, not the owner). Migrations run as `core_owner` in a separate short-lived step (an AppHost resource the API waits for), never inside the API process: owner credentials can disable RLS, so no runtime process holds them.
- The bootstrap's default privileges also cover the EF migrations history table: the first migration revokes `app_core`'s access to `core."__EFMigrationsHistory"`.
- A state change and its outbox message are written in the same transaction. Consumers are idempotent.

## API

- Commands and queries follow the `cqrs-slice` skill. Errors are ProblemDetails.
- Long-running work returns `202 Accepted`; progress goes out through SignalR.
- YARP strips any client-sent tenant or internal headers, then adds the internal token and tenant headers derived from the validated JWT.
- Rate limiting and the per-tenant token budget live here, not in Python.

## Authentication and authorization

- JWT bearer against the Keycloak realm, with the plain `Microsoft.AspNetCore.Authentication.JwtBearer` package; setup in `Normetry.Api/Auth/AuthSetup.cs`. `Authentication:Schemes:Bearer` in `appsettings.json` holds `Authority` (`http://localhost:8180/realms/normetry`) and `ValidAudiences` (`normetry-api`). Override them per environment with `Authentication__Schemes__Bearer__Authority` and `Authentication__Schemes__Bearer__ValidAudiences__0`. The issuer and signing keys come from the authority's metadata.
- Settings honor only `Authority` and `ValidAudiences`. The binder for `Authentication:Schemes:Bearer` also reads `ValidIssuer(s)`, `SigningKeys`, `MetadataAddress`, `Forward*` and the validation flags, so `AuthSetup` replaces or clears all of them after it runs. The issuer and signing keys come only from the authority's metadata. Issuer, audience, lifetime, expiry and signature checks are always on. `RequireHttpsMetadata` is false only in Development.
- In Development the API refuses to start unless the authority is a loopback address: Development accepts `normetry-dev` tokens and plain-http metadata, so it must never run against a deployed identity provider.
- Claims keep their JWT names (`MapInboundClaims = false`). Users are identified by `sub`, never by email. `OnTokenValidated` maps `realm_access.roles` to role claims, keeping only `Owner`, `Manager` and `Auditor`. Only listed clients (`azp`) are accepted: `normetry-web` everywhere and `normetry-dev` in Development only. Any other client, or a token without `azp`, gets 401. The list is a constant in `AuthSetup.cs`, not a setting.
- Policies (`Policies` in `AuthSetup.cs`): the default policy needs an authenticated user with a `sub`. `Tenant` adds exactly one `tenant_id` claim holding a non-empty UUID in 8-4-4-4-12 form. `Owner`, `Manager` and `Auditor` each add their role on top of `Tenant`. Every tenant-scoped endpoint requires `Tenant` or a role policy, so a token without a valid tenant gets 403 before any handler runs. The fallback policy is `Tenant` too, so an endpoint that declares nothing is never anonymous. Anonymous endpoints say so with `.AllowAnonymous()` (today only `/health` and `/alive` in ServiceDefaults) and must be added to the allow-list in `AuthConfigurationTests`, which checks every mapped endpoint.
- Handlers read the caller through `ICurrentUser` (user id, roles) and `ITenantContext` (tenant id) in `Normetry.Application/Abstractions`. `HttpCurrentUser` (Api) implements both from the validated principal only: the tenant never comes from the body, query string or headers. Reading `TenantId` without a valid tenant throws, which means the endpoint is missing its policy.

## Tests

Test projects live in `src/core/tests/`: xUnit v3 on Microsoft.Testing.Platform (selected in the root `global.json`). A test project needs at least one test, because the platform fails a run with zero tests.

- Domain and BuildingBlocks: unit tests (`Normetry.UnitTests`; it references Api for the ProblemDetails mapping).
- Endpoints and auth: `Normetry.IntegrationTests` runs the API in memory with `WebApplicationFactory<Program>` (`ApiFactory`). The factory replaces the authority's metadata with a static configuration that trusts a test issuer and an RSA key generated per run, and `ApiFactory.CreateToken` builds Keycloak-shaped access tokens, so no Keycloak is needed. For environment-specific behavior use `factory.WithWebHostBuilder(b => b.UseEnvironment("Production"))`.
- Handlers and persistence: integration tests with Testcontainers PostgreSQL, RLS included, in `Normetry.IntegrationTests` (from the first persistence work, #12).
- Architecture: NetArchTest rules (`Normetry.ArchitectureTests`).

## Commands

From the repo root:

- `dotnet build` · `dotnet test`
- `dotnet run --project src/core/Normetry.AppHost` (needs Docker): Aspire dashboard at `https://localhost:17081`, login link in the console. Trust the HTTPS dev certificate once with `dotnet dev-certs https --trust`, otherwise the services cannot export telemetry to the dashboard (TLS verify fails). If `mcr.microsoft.com` is unreachable, set `ASPIRE_ENABLE_CONTAINER_TUNNEL=false`: the default container tunnel image is built from it.
- `dotnet tool restore` then `dotnet csharpier check .` (or `format .`)

## Configuration

- Secrets through `dotnet user-secrets`, Aspire parameters or environment variables. `appsettings*.json` holds no secrets.
- The AppHost generates every secret parameter on the first run (`internal-token`, the five role passwords, the Postgres and Keycloak admin passwords) and keeps it in its user secrets. It passes the API `ConnectionStrings__core` (`app_core`), `Ai__InternalToken` and the Keycloak reference (the API's JWT authority comes from `appsettings.json` instead, because the token issuer is the fixed `http://localhost:8180` URL); the AI service gets `NORMETRY_INTERNAL_TOKEN` and `NORMETRY_DATABASE_URL` (`app_ai`). Owner credentials go only to the migration steps (#12, #13). Roles and schemas: `deploy/postgres/README.md`; realm, tenants and demo users: `deploy/keycloak/README.md`.
- The AI service answers 401 on every route without `X-Normetry-Internal-Token`, so its health check is a custom one in `AppHost.cs` that sends the token, not `WithHttpHealthCheck`.
- Formatter: CSharpier as a local dotnet tool (`.config/dotnet-tools.json`); the hook runs it on every edited `.cs` file.
