# 011. Build the web app as a Vite single-page app

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

`src/web` is the first web client: sign-in, an establishment picker and, next, the regulation chat. The reference mock names Next.js, and DESIGN.md left the choice between Next.js and a Vite SPA to an ADR. All data and every rule already live behind the .NET API (ADR 004): it validates the token, sets the tenant and is the only way to the AI service. Nothing on the page is public or needs to be indexed by search engines, and every screen depends on the signed-in user.

The browser has to hold an access token somewhere. A token in `localStorage` or `sessionStorage` can be read by any script that runs through an XSS bug.

## Decision

- The web app is a React and TypeScript single-page app built with Vite. It has no server of its own: in production it is static files.
- It signs in with Keycloak through the public client `normetry-web`, using the authorization code flow with PKCE (`oidc-client-ts`). Tokens stay in memory. Only the PKCE state, which must survive the redirect, goes in `sessionStorage`. The refresh token renews the session in the background, and a reload signs in again through Keycloak's own session.
- The app calls only the core API, on its own origin under `/api`. In development the Vite server forwards `/api` to the API, so the API needs no CORS. A deployed environment serves the static files and the API under one origin in the same way.

## Consequences

- One backend to secure and observe; the web app adds no server-side code, secrets or runtime.
- A reload costs a round trip to Keycloak. In exchange, no token survives in web storage.
- No server rendering: the first paint waits for the JavaScript bundle and sign-in. That is fine for a signed-in tool and would matter only for public pages.
- Deployment needs a reverse proxy (or the API itself) to serve the files and `/api` from one origin. If they end up on different origins, the API needs a CORS policy for exactly that origin.
- We would revisit this for public, indexed pages (a marketing site would be a separate app) or if the web tier needed its own server, for example to keep tokens in an HTTP-only cookie (a backend-for-frontend).

## Alternatives considered

- **Next.js** — its strengths are server rendering, server components and its own API routes. Here every page needs the user's token and the data comes from the .NET API, so it would add a second server with nothing to do and a second place where auth and tenant rules could drift.
- **Backend-for-frontend with HTTP-only cookies** — keeps tokens out of JavaScript entirely, but needs a server-side session store and CSRF protection. Worth it when the threat model asks for it; in-memory tokens with short lifetimes cover this phase.
- **Tokens in `sessionStorage`** (the library default for the user store) — survives a reload without a redirect, but any XSS can read it.
