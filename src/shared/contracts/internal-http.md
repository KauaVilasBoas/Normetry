# Internal HTTP contract: core gateway to AI service

The AI service has no public ingress (ADR 004). Every HTTP request it receives comes through the YARP route `/ai/{**catch-all}` in `Normetry.Api`, which turns the validated JWT into the headers below. Both sides implement this file; a change to it changes both sides in the same pull request.

## Headers

| Header | Value | Source on the core side | Read in Python by |
|---|---|---|---|
| `X-Normetry-Internal-Token` | Shared secret, 32 characters or more | Aspire parameter `internal-token`: `Ai:InternalToken` in the core, `NORMETRY_INTERNAL_TOKEN` in the AI service | `InternalTokenMiddleware` (`api/auth.py`) |
| `X-Normetry-Tenant-Id` | Non-nil UUID, lower-case 8-4-4-4-12 form | `tenant_id` claim | `request_context` (`api/context.py`) |
| `X-Normetry-User-Id` | Keycloak user id | `sub` claim | `request_context` |

Each header appears exactly once on a forwarded request.

Reserved, not sent yet: `X-Normetry-Establishment-Id` and `X-Normetry-Jurisdiction`. The JWT carries neither. A later route that works on one establishment will set them after the core has loaded that establishment under RLS, which proves it belongs to the tenant. Until then the gateway strips them like any other `X-Normetry-*` header, and the AI service must not read them.

## Core gateway rules

- The route requires the `Tenant` policy. A request without a valid token gets 401 and one without a valid tenant gets 403; neither reaches the AI service.
- The `/ai` prefix is removed: `/ai/health` arrives as `/health`.
- Before adding anything, the gateway removes every inbound header whose name starts with `X-Normetry-` (any case, and with underscores read as dashes, so `X_Normetry_Tenant_Id` goes too), plus `Authorization` and `Cookie`. It also drops an `access_token` query parameter. Credentials meant for the core never reach the AI service.
- It then sets the three headers above from the validated token only, never from the body, query string or client headers.
- The destination is `http://ai`, resolved by Aspire service discovery from `Services:ai:http:0`. The core refuses to start without that setting: service discovery would otherwise fall back to a DNS lookup of `ai` and send the token to whatever host answers.
- Every `X-Normetry-*` response header is removed before the response reaches the client.
- Responses are not buffered, so server-sent events stream through.
- The core refuses to start when `Ai:InternalToken` is missing or shorter than 32 characters.

## AI service rules

- The token check runs as ASGI middleware before routing and body parsing: no token, a wrong token or the header twice gets 401 on every path. The comparison is constant time.
- Routes read the caller only through the `request_context` dependency. It requires exactly one tenant header holding a non-nil UUID in lower-case 8-4-4-4-12 form and exactly one non-empty user header, and answers 400 otherwise: a malformed value is a gateway bug, never something to guess around.
- Database work for the request goes through `tenant_transaction(engine, ctx.tenant_id)`. The first route that reads or writes tenant data adds a test proving one tenant cannot see another's rows through it.
- No other header, body field or query parameter identifies the tenant or the user.
