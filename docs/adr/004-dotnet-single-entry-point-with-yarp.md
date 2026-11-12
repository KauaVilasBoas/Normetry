# 004. Make the .NET core the single entry point, with YARP in front of Python

- **Status:** Accepted
- **Date:** 2026-11-11

## Context

The web app needs both services: the core's REST API and SignalR hub, and the AI service's chat and search. Every request needs JWT validation and rate limiting, and every AI request also counts against the tenant's token budget. The AI service is where a request costs the most, because it calls the LLMs. The tenant id must come from the validated token, never from the request body, the query string or a client header.

## Decision

- Clients reach only the .NET core. YARP, hosted in `Normetry.Api`, forwards `/ai/*` to FastAPI.
- The core validates the JWT (OIDC, with Keycloak in local development) and applies ASP.NET Core rate limiting and the per-tenant token budget. YARP then strips any client-sent tenant or internal headers and adds an internal token plus tenant and user headers derived from the validated JWT. Establishment and jurisdiction headers are added only by routes that work on one establishment, after the core has loaded it under RLS; the JWT carries neither. Header names and rules: `src/shared/contracts/internal-http.md`.
- The Python service has no public ingress. FastAPI rejects any request without the internal token and reads the tenant only from the internal headers.
- Chat answers stream over SSE through YARP.

## Consequences

- Auth, rate limiting and the token budget are written and tested once, in one service.
- Chat gets one extra network hop. SSE has to stream through YARP without buffering, and the proxy timeouts have to allow a long answer.
- Every AI request depends on the core being up.
- The internal token is one more secret to rotate, kept out of the repository like the others.
- We would revisit this if the extra hop became a measurable share of chat latency.

## Alternatives considered

- **Expose FastAPI directly, with its own JWT validation** — two auth implementations and two rate limiters to keep in step, and the service that spends LLM tokens becomes reachable from the internet.
- **A dedicated API gateway product (such as Azure API Management)** — more infrastructure to run for proxying a single backend, when the core already validates the JWT.
