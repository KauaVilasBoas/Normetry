import {
  ErrorTimeout,
  InMemoryWebStorage,
  UserManager,
  WebStorageStateStore,
} from "oidc-client-ts";

export const CALLBACK_PATH = "/auth/callback";
export const SIGNED_OUT_PATH = "/signed-out";

/** The UserManager calls the session uses, so tests can pass a fake. */
export type OidcClient = Pick<
  UserManager,
  | "getUser"
  | "signinRedirect"
  | "signinRedirectCallback"
  | "signinSilent"
  | "removeUser"
  | "signoutRedirect"
  | "events"
>;

export function createUserManager(
  authority: string = import.meta.env.VITE_OIDC_AUTHORITY ??
    "http://localhost:8180/realms/normetry",
): UserManager {
  const origin = window.location.origin;
  return new UserManager({
    authority,
    client_id: "normetry-web",
    // Authorization code with PKCE (S256), the library default for response_type "code".
    response_type: "code",
    scope: "openid profile email",
    redirect_uri: `${origin}${CALLBACK_PATH}`,
    post_logout_redirect_uri: `${origin}${SIGNED_OUT_PATH}`,
    // Tokens live in memory only, never in web storage; a reload signs in again through the
    // Keycloak session. Only the short-lived PKCE state, which must survive the redirect, goes in
    // sessionStorage (the library default would be localStorage).
    userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
    stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
    // Renews with the refresh token before the access token expires.
    automaticSilentRenew: true,
  });
}

/** A same-origin relative path to return to after sign-in; anything else becomes "/". */
export function safeReturnPath(value: unknown): string {
  if (typeof value !== "string" || !value.startsWith("/") || /^\/[/\\]/.test(value)) return "/";
  let url: URL;
  try {
    url = new URL(value, window.location.origin);
  } catch {
    return "/";
  }
  if (url.origin !== window.location.origin) return "/";
  // Dot segments can resolve to "//host", which the browser would read as another origin.
  if (url.pathname.startsWith("//")) return "/";
  if (url.pathname === CALLBACK_PATH || url.pathname === SIGNED_OUT_PATH) return "/";
  return `${url.pathname}${url.search}${url.hash}`;
}

/** Keycloak could not be reached at all, as opposed to answering with an error. */
export function isUnreachable(error: unknown): boolean {
  return error instanceof TypeError || error instanceof ErrorTimeout;
}

export function currentPath(): string {
  const { pathname, search, hash } = window.location;
  return `${pathname}${search}${hash}`;
}
