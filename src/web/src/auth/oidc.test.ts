import { ErrorTimeout, InMemoryWebStorage } from "oidc-client-ts";
import { describe, expect, it } from "vitest";
import { createUserManager, isUnreachable, safeReturnPath } from "./oidc";

describe("safeReturnPath", () => {
  it.each([
    ["/", "/"],
    ["/conversations/42?tab=sources#claim-3", "/conversations/42?tab=sources#claim-3"],
    ["/a/../b", "/b"],
  ])("keeps the same-origin relative path %s", (value, expected) => {
    expect(safeReturnPath(value)).toBe(expected);
  });

  it.each([
    ["absolute URL", "https://attacker.example/"],
    ["protocol-relative URL", "//attacker.example/path"],
    ["backslash trick", "/\\attacker.example"],
    ["dot segment to a protocol-relative path", "/.//attacker.example/"],
    ["javascript URL", "javascript:alert(1)"],
    ["relative without slash", "settings"],
    ["the callback itself", "/auth/callback?code=x"],
    ["the signed-out page", "/signed-out"],
    ["not a string", 42],
    ["missing", undefined],
  ])("falls back to / for %s", (_case, value) => {
    expect(safeReturnPath(value)).toBe("/");
  });
});

describe("isUnreachable", () => {
  it("is true for network failures and timeouts only", () => {
    expect(isUnreachable(new TypeError("Failed to fetch"))).toBe(true);
    expect(isUnreachable(new ErrorTimeout("Network timed out"))).toBe(true);
    expect(isUnreachable(new Error("invalid_grant"))).toBe(false);
  });
});

describe("createUserManager", () => {
  it("uses the public normetry-web client with code flow and no tokens in web storage", () => {
    const { settings } = createUserManager("http://localhost:8180/realms/normetry");

    expect(settings.client_id).toBe("normetry-web");
    expect(settings.response_type).toBe("code");
    expect(settings.disablePKCE).toBe(false);
    expect(settings.scope).toBe("openid profile email");
    expect(settings.redirect_uri).toBe(`${window.location.origin}/auth/callback`);
    expect(settings.post_logout_redirect_uri).toBe(`${window.location.origin}/signed-out`);
    expect((settings.userStore as unknown as { _store: Storage })._store).toBeInstanceOf(
      InMemoryWebStorage,
    );
    expect((settings.stateStore as unknown as { _store: Storage })._store).toBe(
      window.sessionStorage,
    );
  });
});
