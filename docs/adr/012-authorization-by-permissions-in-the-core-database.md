# 012. Authorize by permissions stored in the core database

- **Status:** Accepted
- **Date:** 2026-11-16

## Context

Until now the API authorized with three Keycloak realm roles (Owner, Manager, Auditor), mapped from `realm_access.roles` to role claims and checked by role policies. Phase 2a adds documents, and a tenant now needs to decide who may upload, delete or only read, and to manage that itself from the web app. Realm roles are global to the realm: a tenant cannot define its own, and changing a user's role means editing Keycloak, which a tenant administrator cannot reach. The roles also sat in the token, so a change waited for the next token. Every new feature added another combined role policy (`OwnerOrManager`).

## Decision

- Keycloak keeps authentication and the `tenant_id` attribute. Realm roles authorize nothing: the realm defines none, and the API no longer maps them.
- Authorization data lives in schema `core`: a global permission catalog (`core.permissions`, `area.action` names fixed in code and inserted by a data migration) and, per tenant under RLS (ADR 003), profiles, the permissions each profile grants, users (by `sub`) and the profiles each user holds.
- Every tenant has three system profiles with a stable system key: Owner grants the whole catalog through a `grants_all` flag, so a new permission reaches it with no data change, and cannot be changed; Manager and Auditor cannot be renamed or deleted, but the tenant edits their permissions. They are created in the transaction that creates the tenant, never by a migration, because RLS keeps the migration owner out of tenant tables. A tenant always has an Owner: removing the last one is refused.
- Every controller action carries `[HasPermission(Permissions.X.Y)]` (or `[AllowAnonymous]`), and a test fails on any action with neither. A custom policy provider turns the attribute into the `Tenant` policy plus a permission requirement, and its handler loads the caller's permissions per request under the tenant transaction. The YARP `/ai/*` routes require `chat.ask` the same way. A missing permission is a 403 problem with the code `permission.denied`.
- `/me` is the one action that needs only a valid tenant. It returns the caller's profiles and permissions, so the web app hides what the user cannot do; the server still checks every request. A user row is created on first sign-in with no profiles, so the user gets 403 everywhere else until an administrator assigns one.

## Consequences

- Tenants define their own profiles and assign them in the app, and a change applies on the next request, with no new token.
- Each protected request costs one more query (the caller's permissions). There is no cache yet; one needs invalidation when profiles change.
- `profiles.manage` is as strong as Owner: whoever holds it can assign Owner to anyone. That is the intent, and only Owner holds it by default.
- Authorization now depends on the core database being reachable. Without it the API cannot authorize anything except `/me`'s tenant check.
- The startup step that adds missing system profiles can only reach tenants it knows (today the demo tenants): `app_core` cannot list tenants under RLS. Real tenant onboarding (phase 3) creates them in its own transaction.
- We would revisit this for a platform administrator above tenants, which breaks "the tenant comes from the JWT" and needs its own ADR.

## Alternatives considered

- **Keep Keycloak realm roles, add more of them** — tenants cannot manage them, every combination needs a new policy, and changes wait for a new token.
- **Keycloak groups or client roles per tenant, or Keycloak Authorization Services** — keeps one source of truth in the identity provider, but tenant administrators would need Keycloak admin rights or a proxy API to it, and RLS could not protect the data.
- **Permissions as token claims** — no query per request, but the token grows with the catalog and stays stale until it is refreshed.
- **An open-source authorization server (OpenFGA, SpiceDB)** — fits relationship-based access at scale, but is one more service to run for eight permissions and a few profiles per tenant.
