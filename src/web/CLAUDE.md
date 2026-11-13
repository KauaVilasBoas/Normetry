# src/web: React web app

React 19 + TypeScript single-page app built with Vite (ADR 011), managed with npm (`package.json` + `package-lock.json`). The look is fixed by `docs/design/reference.png` and `docs/design/DESIGN.md` (`normetry-design-system` skill); every UI change gets a `ux-ui-designer` screen spec before and a review after.

## Stack

- Vite 8, React 19, TypeScript 6 (`tsc -b` with project references; typescript-eslint does not support TypeScript 7 yet).
- Tailwind CSS 4 with the DESIGN.md tokens as the only theme (`src/index.css`): the default palette, font sizes and radii are reset, so `bg-red-500` or `text-sm` do not exist. Colors: `bg-canvas|sidebar|app|card|inset|elevated`, `border-border`, `bg-bubble-user`, `bg-mint`, `text-on-mint`, `bg-teal`, `bg-teal-deep`, `border-teal-border`, `text-text-primary|secondary|muted`, `text-neutral`, `text-chart-2`, `bg-scrim`. Sizes: `text-caption` 11, `text-small` 12, `text-body` 13, `text-title` 15, `text-greeting` 22, `text-field` 16 (form fields below 640px: `text-field sm:text-body`). Radii: `rounded-sm` 8, `rounded-control` 10, `rounded-card` 12, `rounded-pill`. A missing token goes into DESIGN.md first, then `index.css`.
- Radix primitives (`radix-ui`) for anything with focus or keyboard behavior: Dialog (drawer, sheets), AlertDialog (delete confirmation), Select (establishment switcher), DropdownMenu (account menu). Styled by hand in the shadcn/ui manner; no shadcn CLI or `components.json` yet, add them with the first generated component.
- TanStack Query for server state; React Router 8 in declarative mode (`BrowserRouter` with `useTransitions={false}`, so a navigation commits with the click and focus moved after it lands on the new view); `oidc-client-ts` for sign-in; Lucide icons (stroke 1.5 set globally on `.lucide`); Inter from `@fontsource-variable/inter` (self-hosted).
- Chat answers stream over `fetch` (the bearer token cannot go on an `EventSource`), parsed by the hand-written reader in `chat/answers.ts`; no SSE library.
- Not added yet: Framer Motion (CSS covers today's fades and slides), Recharts (no charts yet), SignalR.

## Layout

- `src/main.tsx` builds the UserManager and the auth store; `src/App.tsx` shows the auth screen or the authenticated app (query client, session context, router).
- `auth/`: `oidc.ts` (UserManager settings, return-path validation, error classification), `session.ts` (the sign-in state machine as a plain store read with `useSyncExternalStore`, plus `useSession`), `AuthScreen.tsx` (every screen outside the shell).
- `api/`: `client.ts` (the only `fetch` to the backend: `request` returns the response, `get` the JSON), `queries.ts` (response types, query hooks, the query client defaults).
- `shell/`: the app frame (`AppShell`), sidebar, establishment switcher, account menu.
- `establishments/`: picker, selection and labels (`selection.ts`).
- `chat/`: Ask the Regulation (`/` new conversation, `/conversations/:id` a saved one). `ChatScreen` (list | conversation | source panel, sheets, focus, the polite live region), `ConversationList`, `ConversationPane` (one per conversation: leaving it aborts the stream), `Answer` (claim reveal, chips, system messages), `Composer` (field, send, quota), `SourcePanel`, and `answers.ts` (server data guards, stored messages to turns, the SSE reader, quota headers).
- `ui/`: shared visual primitives (`Button`, `iconButton` (36px icon-only button classes), `Spinner` (its `surface` prop picks a track that shows on card, elevated or mint), `Skeleton`, `TypeIcon`, `TypeTile`, `Avatar`, `Message`, `CitationChip`, `Sheet`, `Logo`, `LogoMark`) and `media.ts` (`useMediaQuery`, the chat breakpoints). New primitives go here, never inline in a screen.
- `copy.ts`: every interface string, whole sentences with `{placeholders}` filled by `format()`. Never concatenate sentences. English only until the PT switch.
- Tests sit next to the code (`*.test.ts(x)`); shared fakes in `src/test/`.

## Boundaries

- The app calls only the core API, on its own origin under `/api` (`API_BASE_URL` in `api/client.ts`). The Vite dev server forwards `/api/*` to the API (`NORMETRY_API_URL`, set by the AppHost; default `http://localhost:5208`), so the API needs no CORS. Never call the AI service: chat goes through the core's `/ai/*` gateway.
- Sign-in: public Keycloak client `normetry-web`, authorization code with PKCE (S256), scopes `openid profile email`. Authority from `VITE_OIDC_AUTHORITY` (set by the AppHost). Only the dev server falls back to `http://localhost:8180/realms/normetry`; a production bundle without it stops at startup with an error naming the variable. The dev server's port is fixed at 5173 because the client accepts only `http://localhost:5173` (`/auth/callback`, `/signed-out`).
- Tokens live in the UserManager's memory only (`InMemoryWebStorage`), never in `localStorage` or `sessionStorage`. Only the PKCE state, which must survive the redirect, goes in `sessionStorage`. A reload goes back through Keycloak, which signs the user straight back in while its session lasts. Refresh uses the refresh token (`automaticSilentRenew`), never an iframe.
- The API client attaches the bearer token to every request. On 401 it refreshes once (shared between concurrent requests) and retries; a second 401, or a failed refresh, starts the session-expired flow (redirect to Keycloak with the current path). A failed background refresh does the same.
- The return path after sign-in must be a same-origin relative path (`safeReturnPath`); anything else becomes `/`.
- Only claim text and citation data from the server reach the screen, and only after `toClaim` checked them (a claim needs a citation; an official link must be http(s)). Refusal, unavailable and quota text comes from `copy.ts`; unknown stream events are ignored.
- The tenant comes from the token on the server. The UI shows the tenant name from `GET /me` and never sends a tenant id. The selected establishment's id is remembered in `localStorage` per tenant and user as a convenience only; the jurisdiction is never stored or sent for use in requests.
- Nothing secret in this folder: everything shipped to the browser is public. `VITE_*` variables end up in the bundle.

## UI rules

- Tokens only (see Stack). One primary button per view. Status colors only for status; none in #53.
- `--text-muted` only on `--bg-sidebar` and `--bg-inset`; `--neutral` never for text (DESIGN.md contrast rule).
- Every interactive element keeps the global mint focus ring (`:focus-visible` in `index.css`); rows and popover items inset it with `focus-visible:-outline-offset-2`.
- Motion through `motion-safe:` utilities only, so `prefers-reduced-motion` turns it off.
- Page titles with React 19's `<title>` element.

## Tooling

From `src/web`:

- `npm ci` · `npm run dev` · `npm run lint` · `npm run format:check` (`npm run format` to fix) · `npm run typecheck` · `npm test` · `npm run build`. CI runs all of them (job `web` in `.github/workflows/ci.yml`).
- The AppHost (`dotnet run --project src/core/Normetry.AppHost` from the repo root) runs `npm install` and then `npm run dev` with `NORMETRY_API_URL` and `VITE_OIDC_AUTHORITY` set; open `http://localhost:5173` and sign in with a demo user from `deploy/keycloak/README.md`.
- Prettier (`printWidth` 100, `endOfLine` auto because of `core.autocrlf`); the repo hook formats edited `.ts`, `.tsx` and `.css` files. ESLint flat config with typescript-eslint (type-checked), react-hooks (including the React Compiler rules) and react-refresh. `eslint-plugin-jsx-a11y` does not support ESLint 10 yet; accessibility is checked by axe in the tests.
- Tests: Vitest in jsdom with Testing Library and user-event. `expectNoAxeViolations()` runs axe-core on the whole document; every screen state gets one. Stub the API with `stubApi()` (a key may name the method: `"DELETE /ai/conversations/c-1"`), stream answers with `sseResponse()`, set the window width with `mockViewport()` and sign in with `fakeOidc()` (`src/test/fakes.ts`); no test reaches Keycloak or the API.
- Dependencies are added with a release cooldown: `npm install --before=<two weeks ago> <package>`.
