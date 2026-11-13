import axe from "axe-core";
import type { User } from "oidc-client-ts";
import { expect, vi } from "vitest";
import type { OidcClient } from "../auth/oidc";

export function fakeUser(overrides: Partial<User> = {}): User {
  return {
    access_token: "access-1",
    id_token: "id-1",
    expired: false,
    state: undefined,
    profile: {
      sub: "user-1",
      name: "Olivia Owner",
      given_name: "Olivia",
      email: "owner@bistro.example",
    },
    ...overrides,
  } as User;
}

/** A UserManager stand-in: signed in as `user` (null for no session), redirects never resolve. */
export function fakeOidc(user: User | null = fakeUser()) {
  let current = user;
  const renewErrorHandlers: Array<() => void> = [];
  const oidc = {
    getUser: vi.fn(() => Promise.resolve(current)),
    signinRedirect: vi.fn(() => new Promise<void>(() => {})),
    signinRedirectCallback: vi.fn(() => Promise.resolve(fakeUser())),
    signinSilent: vi.fn(() => Promise.reject(new Error("login_required"))),
    removeUser: vi.fn(() => {
      current = null;
      return Promise.resolve();
    }),
    signoutRedirect: vi.fn(() => new Promise<void>(() => {})),
    events: {
      addSilentRenewError: (handler: () => void) => renewErrorHandlers.push(handler),
      removeSilentRenewError: vi.fn(),
    },
  };
  return {
    oidc: oidc as unknown as OidcClient & typeof oidc,
    setUser: (next: User | null) => (current = next),
    failSilentRenew: () => renewErrorHandlers.forEach((handler) => handler()),
  };
}

type Route =
  { status?: number; body?: unknown; headers?: Record<string, string> } | (() => Promise<Response>);

/**
 * Stubs fetch for API paths (without the /api prefix). A key may name the method
 * ("DELETE /ai/conversations/c-1"), which wins over the bare path. Unknown paths answer 404.
 */
export function stubApi(routes: Record<string, Route | Route[]>) {
  const fetch = vi.fn<(input: string, init?: RequestInit) => Promise<Response>>((input, init) => {
    const path = input.replace(/^\/api/, "");
    const entry = routes[`${init?.method ?? "GET"} ${path}`] ?? routes[path];
    const route = Array.isArray(entry) ? (entry.length > 1 ? entry.shift() : entry[0]) : entry;
    if (typeof route === "function") return route();
    return Promise.resolve(
      new Response(route?.body === undefined ? null : JSON.stringify(route.body), {
        status: route?.status ?? (route ? 200 : 404),
        headers: { "Content-Type": "application/json", ...route?.headers },
      }),
    );
  });
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

export const me = {
  userId: "user-1",
  tenantId: "11111111-1111-4111-8111-111111111111",
  roles: ["Owner"],
};

export const establishments = [
  { id: "e-lisboa", name: "Bistro Lisboa", type: "Restaurant", jurisdiction: "EU" },
  { id: "e-sao-paulo", name: "Bistro São Paulo", type: "Restaurant", jurisdiction: "BR" },
];

/**
 * Zero axe violations on the whole document (portals included). With an open popover, the
 * best-practice "region" rule is off: listbox and menu popovers portal to <body>, outside the
 * landmarks, while they are open. Every WCAG rule still runs.
 */
export async function expectNoAxeViolations({ popoverOpen = false } = {}) {
  const { violations } = await axe.run(document.body, {
    rules: popoverOpen ? { region: { enabled: false } } : {},
  });
  expect(
    violations.map(({ id, nodes }) => `${id}: ${nodes.map((n) => n.html).join(", ")}`),
  ).toEqual([]);
}

/** An answer stream the test writes to, event by event. */
export function sseResponse(headers: Record<string, string> = {}) {
  const encoder = new TextEncoder();
  let controller!: ReadableStreamDefaultController<Uint8Array>;
  const body = new ReadableStream<Uint8Array>({
    start: (streamController) => {
      controller = streamController;
    },
  });
  return {
    response: new Response(body, {
      headers: { "Content-Type": "text/event-stream", ...headers },
    }),
    send: (event: string, data: unknown) =>
      controller.enqueue(encoder.encode(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`)),
    close: () => controller.close(),
  };
}

/** Media queries for a window this wide; reduced motion only when asked. */
export function mockViewport(width: number, { reducedMotion = false } = {}) {
  vi.spyOn(window, "matchMedia").mockImplementation((query: string) => {
    const minWidth = /\(min-width: (\d+)px\)/.exec(query);
    return {
      matches: minWidth ? width >= Number(minWidth[1]) : reducedMotion && query.includes("reduce"),
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
    } as unknown as MediaQueryList;
  });
}
