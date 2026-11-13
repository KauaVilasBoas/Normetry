# Keycloak realm (local development only)

`normetry-realm.json` is imported by the AppHost on every start. Keycloak runs at
`http://localhost:8180` with no data volume, so the file is the whole state: edit it and restart.
The admin console login is `admin` with the `keycloak-password` parameter from the AppHost user
secrets (also visible in the Aspire dashboard).

## Tenants

The ids are fixed so the core can seed the same tenants (#12).

| Tenant | `tenant_id` |
|---|---|
| Demo Bistro | `11111111-1111-4111-8111-111111111111` |
| Demo Bakery | `22222222-2222-4222-8222-222222222222` |

## Demo users

Every demo user's password is `local-demo-only`. It is fake and works only against this local
realm; never reuse this realm or its users anywhere else.

| Username | Realm role | Tenant |
|---|---|---|
| `bistro.owner` | `Owner` | Demo Bistro |
| `bistro.manager` | `Manager` | Demo Bistro |
| `bakery.auditor` | `Auditor` | Demo Bakery |

`tenant_id` is a user-profile attribute that only admins can view or edit, so users cannot change
their own tenant from the account console.

## Clients

- `normetry-api`: the API's audience. No login flow is enabled.
- `normetry-web`: public client for the web app (`src/web`). Authorization code flow with PKCE
  (S256 required), no password grant. Redirect URI `http://localhost:5173/auth/callback`,
  post-logout redirect `http://localhost:5173/signed-out` and web origin `http://localhost:5173`,
  the dev server's fixed address and nothing else. Scopes `profile` and `email` (plus the
  built-in `basic`, `roles`, `web-origins` and `acr`); no optional scopes, so it cannot ask for
  `offline_access`. Its access tokens carry the same claims as `normetry-dev` (same two mappers).
- `normetry-dev`: public client with only the password grant, for manual testing with curl. Its
  access tokens carry `aud: normetry-api`, `tenant_id` and the roles in `realm_access.roles`.

```sh
curl -s http://localhost:8180/realms/normetry/protocol/openid-connect/token \
  -d grant_type=password -d client_id=normetry-dev \
  -d username=bistro.owner -d password=local-demo-only
```
