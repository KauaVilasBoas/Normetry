import type { User } from "oidc-client-ts";
import { createContext, use, useEffect, useSyncExternalStore } from "react";
import { createApiClient, type ApiClient } from "../api/client";
import {
  CALLBACK_PATH,
  SIGNED_OUT_PATH,
  currentPath,
  isUnreachable,
  safeReturnPath,
  type OidcClient,
} from "./oidc";

export interface Session {
  profile: { name?: string; givenName?: string; email?: string };
  api: ApiClient;
  signOut: () => Promise<void>;
}

export type AuthState =
  | {
      status:
        | "checking"
        | "redirecting"
        | "expired"
        | "signing-out"
        | "failed"
        | "unreachable"
        | "signed-out";
    }
  | { status: "authenticated"; session: Session };

export type AuthStatus = AuthState["status"];

export type Auth = ReturnType<typeof createAuth>;

/**
 * The sign-in state machine (spec #53, section 3). Tokens stay in the UserManager's memory: on a
 * reload the app redirects to Keycloak, which signs the user straight back in while its session
 * lasts.
 */
export function createAuth(oidc: OidcClient) {
  let state: AuthState = {
    status: window.location.pathname === SIGNED_OUT_PATH ? "signed-out" : "checking",
  };
  const listeners = new Set<() => void>();
  let retryPath = "/";
  let renewing: Promise<string | null> | null = null;
  let started = false;

  const set = (next: AuthState) => {
    state = next;
    listeners.forEach((listener) => listener());
  };

  async function signIn(returnPath: string, status: "redirecting" | "expired" = "redirecting") {
    retryPath = safeReturnPath(returnPath);
    set({ status });
    try {
      await oidc.signinRedirect({ state: retryPath });
    } catch (error) {
      set({ status: isUnreachable(error) ? "unreachable" : "failed" });
    }
  }

  // An API 401 that one refresh did not fix, or a failed background refresh.
  function expire() {
    if (state.status !== "authenticated") return;
    void oidc.removeUser();
    void signIn(currentPath(), "expired");
  }

  // One refresh at a time, shared by concurrent 401s.
  function renew() {
    renewing ??= oidc
      .signinSilent()
      .then(
        (renewed) => renewed?.access_token ?? null,
        () => null,
      )
      .finally(() => {
        renewing = null;
      });
    return renewing;
  }

  async function signOut() {
    set({ status: "signing-out" });
    try {
      const idToken = (await oidc.getUser())?.id_token;
      await oidc.removeUser();
      await oidc.signoutRedirect({ id_token_hint: idToken });
    } catch {
      // Keycloak unreachable: the local session is already gone, so the user is signed out here.
      window.history.replaceState(null, "", SIGNED_OUT_PATH);
      set({ status: "signed-out" });
    }
  }

  const api = createApiClient({
    // The UserManager keeps the current tokens in memory, refreshed ones included.
    getAccessToken: async () => (await oidc.getUser())?.access_token,
    renewSession: renew,
    onSessionExpired: expire,
  });

  const authenticated = ({ profile }: User) =>
    set({
      status: "authenticated",
      session: {
        profile: { name: profile.name, givenName: profile.given_name, email: profile.email },
        api,
        signOut,
      },
    });

  /** Runs once per page load (StrictMode calls it twice; the callback state is single-use). */
  function start() {
    if (started) return;
    started = true;
    oidc.events.addSilentRenewError(expire);
    if (state.status !== "checking") return;

    if (window.location.pathname === CALLBACK_PATH) {
      oidc.signinRedirectCallback().then(
        (user) => {
          // Drops code and state from the URL and goes back where the user started.
          window.history.replaceState(null, "", safeReturnPath(user.state));
          authenticated(user);
        },
        (error: unknown) => {
          window.history.replaceState(null, "", "/");
          set({ status: isUnreachable(error) ? "unreachable" : "failed" });
        },
      );
      return;
    }

    void oidc.getUser().then((user) => {
      if (user && !user.expired) authenticated(user);
      else void signIn(currentPath());
    });
  }

  return {
    getState: () => state,
    subscribe: (listener: () => void) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    start,
    retry: () => void signIn(retryPath),
    signIn: () => void signIn("/"),
  };
}

export function useAuth(auth: Auth): AuthState {
  const state = useSyncExternalStore(auth.subscribe, auth.getState);
  useEffect(() => auth.start(), [auth]);
  return state;
}

export const SessionContext = createContext<Session | null>(null);

export function useSession(): Session {
  const session = use(SessionContext);
  if (!session) throw new Error("useSession needs an authenticated session.");
  return session;
}
