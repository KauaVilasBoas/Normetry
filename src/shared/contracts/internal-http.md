# Internal HTTP contract: core gateway to AI service

The AI service has no public ingress (ADR 004). Every HTTP request it receives comes through the YARP routes `/ai/{**catch-all}` and `/ai/establishments/{**rest}` in `Normetry.Api`, which turn the validated JWT, and on establishment routes the establishment the core loaded, into the headers below. Both sides implement this file; a change to it changes both sides in the same pull request.

## Headers

| Header | Value | Source on the core side | Read in Python by |
|---|---|---|---|
| `X-Normetry-Internal-Token` | Shared secret, 32 characters or more | Aspire parameter `internal-token`: `Ai:InternalToken` in the core, `NORMETRY_INTERNAL_TOKEN` in the AI service | `InternalTokenMiddleware` (`api/auth.py`) |
| `X-Normetry-Tenant-Id` | Non-nil UUID, lower-case 8-4-4-4-12 form | `tenant_id` claim | `request_context` (`api/context.py`) |
| `X-Normetry-User-Id` | Keycloak user id | `sub` claim | `request_context` |
| `X-Normetry-Establishment-Id` | Non-nil UUID, lower-case 8-4-4-4-12 form; only on establishment routes | the establishment the core loaded under RLS from the path | establishment routes (#51) |
| `X-Normetry-Jurisdiction` | `US`, `EU` or `BR`; only on establishment routes | that establishment's `jurisdiction` column | establishment routes (#51) |

Each header appears exactly once on a forwarded request; the last two only on establishment routes, and never on any other route. The JWT carries neither establishment nor jurisdiction: the core reads them from `core.establishments` under RLS, which proves the establishment belongs to the caller's tenant.

## Routes

| Browser calls | AI service receives | Establishment headers |
|---|---|---|
| `POST /ai/establishments/{establishment_id}/conversations` | `POST /establishments/{establishment_id}/conversations` | yes |
| `POST /ai/establishments/{establishment_id}/conversations/{conversation_id}/messages` (answered with SSE) | `POST /establishments/{establishment_id}/conversations/{conversation_id}/messages` | yes; counted against the daily quota |
| anything else under `/ai/establishments/{establishment_id}/` | the same path without `/ai` | yes |
| `GET /ai/conversations`, `GET /ai/conversations/{conversation_id}`, `DELETE /ai/conversations/{conversation_id}` | the same path without `/ai` | no |

Any other `/ai/*` path is forwarded the same way, without establishment headers.

`POST /establishments/{establishment_id}/conversations/{conversation_id}/messages` is the only route that generates an answer (and so calls a model). The core counts exactly that route against the daily quota. A new route that generates an answer, or makes the AI service call a model, needs a core change in the same pull request so that it is counted too; until then it would bypass the quota.

## Core gateway rules

- The route requires the `Tenant` policy. A request without a valid token gets 401 and one without a valid tenant gets 403; neither reaches the AI service.
- The `/ai` prefix is removed: `/ai/health` arrives as `/health`.
- Before adding anything, the gateway removes every inbound header whose name starts with `X-Normetry-` (any case, and with underscores read as dashes, so `X_Normetry_Tenant_Id` goes too), plus `Authorization` and `Cookie`. It also drops an `access_token` query parameter. Credentials meant for the core never reach the AI service.
- It then sets the tenant, user and token headers from the validated token only, never from the body, query string or client headers.
- On `/ai/establishments/{establishment_id}/...` it first loads the establishment in a transaction scoped to the caller's tenant. An id that is malformed (anything but the lower-case 8-4-4-4-12 form, so the forwarded path holds exactly the header value), unknown or another tenant's gets 404 and nothing is forwarded. So does a path with an encoded slash or percent (`%2F`, `%252F`) or an empty segment (`//`; one trailing slash is fine), which the AI service could route differently from the core.
- Establishment paths are forwarded only by that route. On any other `/ai/*` path, a first non-empty segment of `establishments` (any case, as in `/ai//establishments/...`) or a `%` left after decoding gets 404. Otherwise it sets `X-Normetry-Establishment-Id` and `X-Normetry-Jurisdiction` from that row.
- `POST .../conversations/{conversation_id}/messages` (any case, trailing slash included) counts one message for the caller's tenant, user and UTC day before forwarding, so a message the AI service then fails on still counts. Over the plan's daily limit the core answers 429 with `Retry-After` (seconds to the next UTC midnight) and forwards nothing. Counted responses carry `RateLimit-Limit`, `RateLimit-Remaining` and `RateLimit-Reset` (seconds to the reset), set as the response starts, so they replace any the AI service sent and the stream is not buffered. Unlimited plans get none of the three. Plans and limits: `Chat` in the core's configuration.
- The destination is `http://ai`, resolved by Aspire service discovery from `Services:ai:http:0`. The core refuses to start without that setting: service discovery would otherwise fall back to a DNS lookup of `ai` and send the token to whatever host answers.
- Every `X-Normetry-*` response header is removed before the response reaches the client.
- Responses are not buffered, so server-sent events stream through.
- The core refuses to start when `Ai:InternalToken` is missing or shorter than 32 characters.

## AI service rules

- The token check runs as ASGI middleware before routing and body parsing: no token, a wrong token or the header twice gets 401 on every path. The comparison is constant time.
- Routes read the caller only through the `request_context` dependency. It requires exactly one tenant header holding a non-nil UUID in lower-case 8-4-4-4-12 form and exactly one non-empty user header, and answers 400 otherwise: a malformed value is a gateway bug, never something to guess around.
- Database work for the request goes through `tenant_transaction(engine, ctx.tenant_id)`. The first route that reads or writes tenant data adds a test proving one tenant cannot see another's rows through it.
- Routes under `/establishments/{establishment_id}/` take the establishment and its jurisdiction only from `X-Normetry-Establishment-Id` and `X-Normetry-Jurisdiction`, which are authoritative: the core checked the establishment belongs to the tenant. Any `/establishments/...` request whose establishment or jurisdiction header is missing, repeated, malformed (a non-nil lower-case UUID and one of `US`, `EU`, `BR` are expected) or whose establishment id does not match the path's `establishment_id` gets 400. Such a request did not come through the establishment route and must not be served.
- Routes outside `/establishments/` must not read the establishment headers.
- The AI service does not count or limit chat messages; the core does, before forwarding.
- No other header, body field or query parameter identifies the tenant, the user, the establishment or the jurisdiction.
