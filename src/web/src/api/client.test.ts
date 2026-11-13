import { describe, expect, it, vi } from "vitest";
import { createAuth } from "../auth/session";
import { fakeOidc, fakeUser, stubApi } from "../test/fakes";
import { ApiError, createApiClient } from "./client";

function client(renewed: string | null = null) {
  const options = {
    getAccessToken: vi.fn(() => Promise.resolve<string | undefined>("access-1")),
    renewSession: vi.fn(() => Promise.resolve(renewed)),
    onSessionExpired: vi.fn(),
  };
  return { api: createApiClient(options), ...options };
}

const authorization = (fetch: ReturnType<typeof stubApi>, call: number) =>
  new Headers(fetch.mock.calls[call][1]!.headers).get("Authorization");

describe("createApiClient", () => {
  it("calls the core API under /api with the bearer token", async () => {
    const fetch = stubApi({ "/me": { body: { userId: "user-1" } } });
    const { api } = client();

    await expect(api.get("/me")).resolves.toEqual({ userId: "user-1" });

    expect(fetch).toHaveBeenCalledWith("/api/me", expect.anything());
    expect(authorization(fetch, 0)).toBe("Bearer access-1");
  });

  it("refreshes once on 401 and retries with the new token", async () => {
    const fetch = stubApi({ "/me": [{ status: 401 }, { body: { userId: "user-1" } }] });
    const { api, renewSession, onSessionExpired } = client("access-2");

    await expect(api.get("/me")).resolves.toEqual({ userId: "user-1" });

    expect(renewSession).toHaveBeenCalledTimes(1);
    expect(authorization(fetch, 1)).toBe("Bearer access-2");
    expect(onSessionExpired).not.toHaveBeenCalled();
  });

  it("starts the session-expired flow when the refresh fails", async () => {
    const fetch = stubApi({ "/me": { status: 401 } });
    const { api, onSessionExpired } = client(null);

    await expect(api.get("/me")).rejects.toEqual(new ApiError(401));

    expect(fetch).toHaveBeenCalledTimes(1);
    expect(onSessionExpired).toHaveBeenCalledTimes(1);
  });

  it("starts the session-expired flow when the API still answers 401 after a refresh", async () => {
    stubApi({ "/me": { status: 401 } });
    const { api, renewSession, onSessionExpired } = client("access-2");

    await expect(api.get("/me")).rejects.toEqual(new ApiError(401));

    expect(renewSession).toHaveBeenCalledTimes(1);
    expect(onSessionExpired).toHaveBeenCalledTimes(1);
  });

  it("reports other failures without touching the session", async () => {
    stubApi({ "/establishments": { status: 503 } });
    const { api, renewSession, onSessionExpired } = client();

    await expect(api.get("/establishments")).rejects.toMatchObject({ status: 503 });

    expect(renewSession).not.toHaveBeenCalled();
    expect(onSessionExpired).not.toHaveBeenCalled();
  });
});

describe("the session's API client", () => {
  it("shares one refresh between concurrent 401s, then expires the session", async () => {
    window.history.replaceState(null, "", "/somewhere");
    stubApi({ "/me": { status: 401 }, "/establishments": { status: 401 } });
    const { oidc } = fakeOidc(fakeUser());
    const auth = createAuth(oidc);
    auth.start();
    await vi.waitFor(() => expect(auth.getState().status).toBe("authenticated"));
    const state = auth.getState();
    if (state.status !== "authenticated") throw new Error("not signed in");

    const results = await Promise.allSettled([
      state.session.api.get("/me"),
      state.session.api.get("/establishments"),
    ]);

    expect(results.map((result) => result.status)).toEqual(["rejected", "rejected"]);
    expect(oidc.signinSilent).toHaveBeenCalledTimes(1);
    expect(auth.getState().status).toBe("expired");
    await vi.waitFor(() =>
      expect(oidc.signinRedirect).toHaveBeenCalledExactlyOnceWith({ state: "/somewhere" }),
    );
  });
});
