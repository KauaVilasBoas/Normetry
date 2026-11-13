import { describe, expect, it, vi } from "vitest";
import { fakeOidc, fakeUser } from "../test/fakes";
import { createAuth } from "./session";

const at = (path: string) => window.history.replaceState(null, "", path);

describe("createAuth", () => {
  it("redirects to sign in with the current path when there is no session", async () => {
    at("/somewhere?x=1#y");
    const { oidc } = fakeOidc(null);
    const auth = createAuth(oidc);

    auth.start();

    await vi.waitFor(() => expect(auth.getState().status).toBe("redirecting"));
    expect(oidc.signinRedirect).toHaveBeenCalledWith({ state: "/somewhere?x=1#y" });
  });

  it("starts only once (StrictMode runs effects twice)", async () => {
    const { oidc } = fakeOidc(null);
    const auth = createAuth(oidc);

    auth.start();
    auth.start();

    await vi.waitFor(() => expect(oidc.signinRedirect).toHaveBeenCalledTimes(1));
    expect(oidc.getUser).toHaveBeenCalledTimes(1);
  });

  it("shows the unreachable state when Keycloak cannot be reached, and retries the same path", async () => {
    at("/somewhere");
    const { oidc } = fakeOidc(null);
    oidc.signinRedirect.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    const auth = createAuth(oidc);

    auth.start();
    await vi.waitFor(() => expect(auth.getState().status).toBe("unreachable"));
    auth.retry();

    expect(oidc.signinRedirect).toHaveBeenLastCalledWith({ state: "/somewhere" });
  });

  it("completes the callback, drops code and state from the URL and returns to the stored path", async () => {
    at("/auth/callback?code=abc&state=xyz");
    const { oidc } = fakeOidc(null);
    oidc.signinRedirectCallback.mockResolvedValueOnce(fakeUser({ state: "/conversations/7?q=1" }));
    const auth = createAuth(oidc);

    auth.start();

    await vi.waitFor(() => expect(auth.getState().status).toBe("authenticated"));
    expect(`${window.location.pathname}${window.location.search}`).toBe("/conversations/7?q=1");
  });

  it("never returns to another origin after the callback", async () => {
    at("/auth/callback?code=abc&state=xyz");
    const { oidc } = fakeOidc(null);
    oidc.signinRedirectCallback.mockResolvedValueOnce(fakeUser({ state: "//attacker.example" }));
    const auth = createAuth(oidc);

    auth.start();

    await vi.waitFor(() => expect(auth.getState().status).toBe("authenticated"));
    expect(window.location.href).toBe(`${window.location.origin}/`);
  });

  it.each([
    ["an error from Keycloak", new Error("access_denied"), "failed"],
    ["a state mismatch", new Error("No matching state found in storage"), "failed"],
    ["a network failure", new TypeError("Failed to fetch"), "unreachable"],
  ])("shows the right screen after %s on the callback", async (_case, error, status) => {
    at("/auth/callback?error=access_denied&state=xyz");
    const { oidc } = fakeOidc(null);
    oidc.signinRedirectCallback.mockRejectedValueOnce(error);
    const auth = createAuth(oidc);

    auth.start();

    await vi.waitFor(() => expect(auth.getState().status).toBe(status));
    expect(window.location.search).toBe("");
    auth.retry();
    expect(oidc.signinRedirect).toHaveBeenCalledWith({ state: "/" });
  });

  it("shows the signed-out screen on its page without redirecting", () => {
    at("/signed-out");
    const { oidc } = fakeOidc(null);
    const auth = createAuth(oidc);

    auth.start();

    expect(auth.getState().status).toBe("signed-out");
    expect(oidc.signinRedirect).not.toHaveBeenCalled();
  });

  it("signs out by clearing the local session first, then ending the Keycloak session", async () => {
    const { oidc } = fakeOidc();
    const auth = createAuth(oidc);
    auth.start();
    await vi.waitFor(() => expect(auth.getState().status).toBe("authenticated"));
    const state = auth.getState();

    void (state.status === "authenticated" && state.session.signOut());

    expect(auth.getState().status).toBe("signing-out");
    await vi.waitFor(() =>
      expect(oidc.signoutRedirect).toHaveBeenCalledWith({ id_token_hint: "id-1" }),
    );
    expect(oidc.removeUser.mock.invocationCallOrder[0]).toBeLessThan(
      oidc.signoutRedirect.mock.invocationCallOrder[0],
    );
  });

  it("shows signed out anyway when Keycloak is unreachable on sign-out", async () => {
    const { oidc } = fakeOidc();
    oidc.signoutRedirect.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    const auth = createAuth(oidc);
    auth.start();
    await vi.waitFor(() => expect(auth.getState().status).toBe("authenticated"));
    const state = auth.getState();

    await (state.status === "authenticated" && state.session.signOut());

    expect(auth.getState().status).toBe("signed-out");
    expect(window.location.pathname).toBe("/signed-out");
    expect(oidc.removeUser).toHaveBeenCalled();
  });

  it("expires the session when a background refresh fails, returning to the current path", async () => {
    at("/somewhere");
    const { oidc, failSilentRenew } = fakeOidc();
    const auth = createAuth(oidc);
    auth.start();
    await vi.waitFor(() => expect(auth.getState().status).toBe("authenticated"));

    failSilentRenew();

    expect(auth.getState().status).toBe("expired");
    expect(oidc.removeUser).toHaveBeenCalled();
    await vi.waitFor(() =>
      expect(oidc.signinRedirect).toHaveBeenCalledWith({ state: "/somewhere" }),
    );
  });
});
