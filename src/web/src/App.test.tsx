import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { User } from "oidc-client-ts";
import { describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { createAuth } from "./auth/session";
import {
  establishments,
  expectNoAxeViolations,
  fakeOidc,
  fakeUser,
  me,
  stubApi,
} from "./test/fakes";

const storageKey = `normetry.establishment.${me.tenantId}.${me.userId}`;
const never = () => new Promise<Response>(() => {});

// The chat screen's own requests; its states are tested in chat/ChatScreen.test.tsx.
const chatRoutes = {
  "/ai/conversations": { body: [] },
  "/chat/quota": { body: { limit: null, remaining: null, resetsAt: "2026-11-14T00:00:00Z" } },
};

function renderApp(routes: Parameters<typeof stubApi>[0], user = fakeOidc()) {
  const fetch = stubApi({ ...chatRoutes, ...routes });
  const auth = createAuth(user.oidc);
  render(<App auth={auth} />);
  return { fetch, auth, oidc: user.oidc };
}

const picker = () => screen.getByRole("region", { name: "Establishments" });
const switcherNone = () => screen.getAllByRole("button", { name: "No establishment" })[0];

describe("auth screens", () => {
  it("shows a status row while signing in on load", async () => {
    const user = fakeOidc(null);
    renderApp({}, user);

    await waitFor(() => expect(user.oidc.signinRedirect).toHaveBeenCalled());
    expect(screen.getByRole("status")).toHaveTextContent("Signing you in…");
    expect(document.title).toBe("Signing in · Normetry");
    await expectNoAxeViolations();
  });

  it("shows an action card when sign-in fails, and Try again restarts it", async () => {
    window.history.replaceState(null, "", "/auth/callback?error=access_denied&state=x");
    const user = fakeOidc(null);
    user.oidc.signinRedirectCallback.mockRejectedValueOnce(new Error("access_denied"));
    renderApp({}, user);

    expect(await screen.findByRole("heading", { name: "Sign-in didn't complete" })).toBeVisible();
    await expectNoAxeViolations();
    await userEvent.click(screen.getByRole("button", { name: "Try again" }));

    expect(screen.getByRole("status")).toHaveTextContent("Redirecting to sign in…");
    expect(user.oidc.signinRedirect).toHaveBeenCalledWith({ state: "/" });
  });

  it("offers Sign in on the signed-out page", async () => {
    window.history.replaceState(null, "", "/signed-out");
    const user = fakeOidc(null);
    renderApp({}, user);

    expect(screen.getByRole("heading", { name: "You're signed out" })).toBeVisible();
    expect(document.title).toBe("Signed out · Normetry");
    await expectNoAxeViolations();
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(user.oidc.signinRedirect).toHaveBeenCalledWith({ state: "/" });
  });
});

describe("establishment picker", () => {
  it("shows only the switcher skeleton and a busy label while loading", async () => {
    renderApp({ "/me": never, "/establishments": never });

    expect(await screen.findByRole("button", { name: "Loading establishments…" })).toHaveAttribute(
      "aria-disabled",
      "true",
    );
    expect(within(screen.getByRole("main")).getByText("Loading establishments…")).toHaveClass(
      "sr-only",
    );
    expect(screen.queryByRole("region", { name: "Establishments" })).toBeNull();
    await expectNoAxeViolations();
  });

  it("selects the first establishment when none is remembered, without asking", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });

    expect(await screen.findByText("Bistro Lisboa · Regulation (EC) 852/2004")).toBeVisible();
    expect(screen.getByRole("combobox", { name: "Establishment" })).toHaveTextContent(
      "Bistro Lisboa",
    );
    expect(screen.queryByRole("region", { name: "Establishments" })).toBeNull();
    expect(localStorage.getItem(storageKey)).toBeNull();
    expect(document.title).toBe("Ask the Regulation · Normetry");
    await expectNoAxeViolations();
  });

  it("shows unknown enum values as sent", async () => {
    renderApp({
      "/me": { body: me },
      "/establishments": {
        body: [
          ...establishments,
          { id: "e-x", name: "Lab", type: "Laboratory", jurisdiction: "CA" },
        ],
      },
    });
    const user = userEvent.setup();

    (await screen.findByRole("combobox", { name: "Establishment" })).focus();
    await user.keyboard("{Enter}");

    expect(await screen.findByRole("option", { name: "Lab" })).toHaveAccessibleDescription(
      "Laboratory · CA",
    );
  });

  it("restores the remembered establishment", async () => {
    localStorage.setItem(storageKey, "e-sao-paulo");
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });

    expect(await screen.findByText("Bistro São Paulo · Anvisa RDC 216/2004")).toBeVisible();
    expect(screen.getByRole("heading", { level: 2, name: "New conversation" })).not.toHaveFocus();
    expect(screen.getByRole("combobox", { name: "Establishment" })).toHaveTextContent(
      "Bistro São Paulo",
    );
  });

  it("clears a remembered id that is no longer in the list and selects the first", async () => {
    localStorage.setItem(storageKey, "e-gone");
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });

    expect(await screen.findByText("Bistro Lisboa · Regulation (EC) 852/2004")).toBeVisible();
    await waitFor(() => expect(localStorage.getItem(storageKey)).toBeNull());
  });

  it("shows the empty state", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: [] } });

    expect(await screen.findByRole("heading", { name: "No establishments yet" })).toBeVisible();
    expect(switcherNone()).toHaveAttribute("aria-disabled", "true");
    await expectNoAxeViolations();
  });

  it("shows the error state and recovers with Try again", async () => {
    let answer: (response: Response) => void = () => {};
    const pending = new Promise<Response>((resolve) => {
      answer = resolve;
    });
    renderApp({
      "/me": { body: me },
      "/establishments": [{ status: 503 }, { status: 503 }, () => pending],
    });

    const alert = await screen.findByRole("alert", {}, { timeout: 3000 });
    expect(alert).toHaveTextContent("Couldn't load establishments");
    expect(switcherNone()).toHaveAttribute("aria-disabled", "true");
    await expectNoAxeViolations();

    const tryAgain = within(alert).getByRole("button", { name: "Try again" });
    await userEvent.click(tryAgain);

    await waitFor(() => expect(tryAgain).toHaveAttribute("aria-disabled", "true"));
    expect(tryAgain).toHaveFocus();
    expect(picker()).toHaveAttribute("aria-busy", "true");
    answer(Response.json(establishments));
    expect(await screen.findByText("Bistro Lisboa · Regulation (EC) 852/2004")).toBeVisible();
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "New conversation" })).toHaveFocus(),
    );
  });

  it("shows the error state when /me fails, even with a single establishment", async () => {
    renderApp({
      "/me": [{ status: 503 }, { status: 503 }, { body: me }],
      "/establishments": { body: [establishments[0]] },
    });

    const alert = await screen.findByRole("alert", {}, { timeout: 3000 });
    expect(screen.queryByText("Bistro Lisboa · Regulation (EC) 852/2004")).toBeNull();
    await userEvent.click(within(alert).getByRole("button", { name: "Try again" }));

    expect(await screen.findByText("Bistro Lisboa · Regulation (EC) 852/2004")).toBeVisible();
  });

  it("explains a missing organization on 403", async () => {
    renderApp({ "/me": { status: 403 }, "/establishments": { status: 403 } });

    expect(
      await screen.findByRole("heading", {
        name: "Your account isn't linked to an organization",
      }),
    ).toBeVisible();
    expect(screen.queryByRole("button", { name: "Try again" })).toBeNull();
    await expectNoAxeViolations();
  });

  it("goes to the session-expired flow on a 401 that a refresh does not fix", async () => {
    const { oidc } = renderApp({ "/me": { status: 401 }, "/establishments": { status: 401 } });

    expect(await screen.findByText("Your session expired. Redirecting to sign in…")).toBeVisible();
    expect(oidc.signinSilent).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(oidc.signinRedirect).toHaveBeenCalledWith({ state: "/" }));
  });
});

describe("shell", () => {
  it("shows the tenant name once /me has it, and hides the label before", async () => {
    renderApp({
      "/me": { body: { ...me, tenantName: "Demo Bistro" } },
      "/establishments": { body: establishments },
    });

    expect(await screen.findByText("Organization:")).toHaveClass("sr-only");
    expect(screen.getAllByText("Demo Bistro")[0]).toBeVisible();
  });

  it("has no tenant label when /me has no tenantName", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });

    await screen.findByRole("combobox", { name: "Establishment" });
    expect(screen.queryByText("Organization:")).toBeNull();
  });

  it("has the landmarks, the skip link and the active nav item", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });

    await screen.findByRole("combobox", { name: "Establishment" });
    expect(screen.getByRole("link", { name: "Skip to main content" })).toHaveAttribute(
      "href",
      "#main",
    );
    expect(screen.getByRole("main")).toHaveAttribute("id", "main");
    expect(screen.getByRole("heading", { level: 1, name: "Ask the Regulation" })).toBeVisible();
    expect(
      within(screen.getByRole("navigation", { name: "Main" })).getByRole("link", {
        name: "Ask the Regulation",
      }),
    ).toHaveAttribute("aria-current", "page");
  });

  it("changes the establishment from the switcher with the keyboard and announces it", async () => {
    localStorage.setItem(storageKey, "e-lisboa");
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });
    const user = userEvent.setup();

    const switcher = await screen.findByRole("combobox", { name: "Establishment" });
    await waitFor(() => expect(switcher).toHaveTextContent("Bistro Lisboa"));
    expect(switcher).toHaveAttribute("aria-haspopup", "listbox");
    switcher.focus();
    await user.keyboard("{Enter}");

    const listbox = await screen.findByRole("listbox");
    const current = within(listbox).getByRole("option", { name: "Bistro Lisboa" });
    expect(current).toHaveAttribute("aria-selected", "true");
    expect(current).toHaveAccessibleDescription(
      "Restaurant · European Union · Regulation (EC) 852/2004",
    );
    await expectNoAxeViolations({ popoverOpen: true });
    await user.keyboard("{ArrowDown}{Enter}");

    await waitFor(() => expect(screen.queryByRole("listbox")).toBeNull());
    expect(screen.getByText("Bistro São Paulo selected")).toHaveAttribute("aria-live", "polite");
    expect(switcher).toHaveFocus();
    expect(switcher).toHaveTextContent("Bistro São Paulo");
    expect(screen.getByText("Bistro São Paulo · Anvisa RDC 216/2004")).toBeVisible();
    expect(localStorage.getItem(storageKey)).toBe("e-sao-paulo");
  });

  it("closes the drawer after a choice in it and focuses the conversation", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });
    const user = userEvent.setup();
    await screen.findByRole("combobox", { name: "Establishment" });

    await user.click(screen.getByRole("button", { name: "Open navigation" }));
    const drawer = await screen.findByRole("dialog", { name: "Navigation" });
    expect(within(drawer).getByRole("button", { name: "Close navigation" })).toBeVisible();
    await expectNoAxeViolations();
    const drawerSwitcher = within(drawer).getByRole("combobox", { name: "Establishment" });
    expect(drawerSwitcher).toHaveTextContent("Bistro Lisboa");
    drawerSwitcher.focus();
    await user.keyboard("{Enter}");
    await screen.findByRole("listbox");
    await user.keyboard("{ArrowDown}{Enter}");

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "New conversation" })).toHaveFocus(),
    );
    expect(screen.getByText("Bistro São Paulo · Anvisa RDC 216/2004")).toBeVisible();
  });

  it("closes the drawer when the window widens to the desktop layout", async () => {
    // Every media query listener (the chat has its own), so the shell's hears the change too.
    const desktop = { matches: false, listeners: [] as (() => void)[] };
    vi.spyOn(window, "matchMedia").mockImplementation(
      () =>
        ({
          get matches() {
            return desktop.matches;
          },
          addEventListener: (_type: string, listener: () => void) =>
            desktop.listeners.push(listener),
          removeEventListener: () => {},
        }) as unknown as MediaQueryList,
    );
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });
    await screen.findByRole("combobox", { name: "Establishment" });
    await userEvent.click(screen.getByRole("button", { name: "Open navigation" }));
    await screen.findByRole("dialog", { name: "Navigation" });

    desktop.matches = true;
    act(() => desktop.listeners.forEach((listener) => listener()));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("returns focus to the menu button when the drawer is dismissed", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });
    const user = userEvent.setup();
    await screen.findByRole("combobox", { name: "Establishment" });

    await user.click(screen.getByRole("button", { name: "Open navigation" }));
    await screen.findByRole("dialog", { name: "Navigation" });
    await user.keyboard("{Escape}");

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Open navigation" })).toHaveFocus(),
    );
  });

  it.each([
    [
      "only an email",
      { sub: "user-1", email: "owner@bistro.example" },
      "Account menu, owner@bistro.example",
      1,
    ],
    ["neither name nor email", { sub: "user-1" }, "Account menu", 0],
  ])("labels the account menu for a profile with %s", async (_case, profile, label, emails) => {
    renderApp(
      { "/me": { body: me }, "/establishments": { body: establishments } },
      fakeOidc(fakeUser({ profile: profile as User["profile"] })),
    );

    const card = await screen.findByRole("button", { name: label });
    expect(within(card).queryAllByText("owner@bistro.example")).toHaveLength(emails);
  });

  it("signs out from the account menu and forgets the establishment", async () => {
    localStorage.setItem(storageKey, "e-lisboa");
    const { oidc } = renderApp({
      "/me": { body: me },
      "/establishments": { body: establishments },
    });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Account menu, Olivia Owner" }));
    const menu = await screen.findByRole("menu");
    expect(menu).toHaveTextContent("owner@bistro.example");
    expect(menu).toHaveTextContent("Owner");
    await expectNoAxeViolations({ popoverOpen: true });
    await user.click(within(menu).getByRole("menuitem", { name: "Sign out" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Signing you out…");
    await waitFor(() => expect(oidc.signoutRedirect).toHaveBeenCalled());
    expect(oidc.removeUser).toHaveBeenCalled();
    expect(localStorage.getItem(storageKey)).toBeNull();
  });
});

describe("permissions", () => {
  const without = (...names: string[]) => ({
    ...me,
    permissions: me.permissions.filter((name) => !names.includes(name)),
  });
  const called = (fetch: ReturnType<typeof stubApi>, path: string) =>
    fetch.mock.calls.some(([input]) => input === `/api${path}`);

  it("shows two skeleton bars instead of the nav while /me is pending", async () => {
    renderApp({ "/me": never });

    await screen.findByRole("button", { name: "Loading establishments…" });
    expect(screen.queryByRole("navigation", { name: "Main" })).toBeNull();
    expect(screen.queryByRole("link", { name: "Ask the Regulation" })).toBeNull();
  });

  it("shows no access, and no switcher or nav, to a user with no profile", async () => {
    const { fetch } = renderApp({
      "/me": { body: { ...me, permissions: [], profiles: [] } },
      "/establishments": { body: establishments },
    });

    expect(
      await screen.findByRole("heading", { level: 2, name: "You don't have access yet" }),
    ).toBeVisible();
    expect(screen.queryByRole("navigation", { name: "Main" })).toBeNull();
    expect(screen.queryByRole("combobox", { name: "Establishment" })).toBeNull();
    expect(screen.queryByRole("button", { name: "No establishment" })).toBeNull();
    expect(called(fetch, "/establishments")).toBe(false);
    expect(called(fetch, "/ai/conversations")).toBe(false);
    await expectNoAxeViolations();
  });

  it("refuses Ask the Regulation by URL without chat.ask", async () => {
    window.history.replaceState(null, "", "/conversations/c-1");
    const { fetch } = renderApp({
      "/me": { body: without("chat.ask") },
      "/establishments": { body: establishments },
    });

    expect(
      await screen.findByRole("heading", { name: "You don't have access to Ask the Regulation" }),
    ).toBeVisible();
    expect(screen.queryByRole("link", { name: "Ask the Regulation" })).toBeNull();
    expect(called(fetch, "/ai/conversations")).toBe(false);
    await expectNoAxeViolations();
  });

  it.each(["/", "/settings/profiles"])(
    "tells a user whose profile opens no screen, at %s",
    async (path) => {
      window.history.replaceState(null, "", path);
      const { fetch } = renderApp({
        "/me": {
          body: {
            ...me,
            permissions: ["establishments.read"],
            profiles: [{ id: "p-kitchen", name: "Kitchen lead", systemKey: null }],
          },
        },
        "/establishments": { body: establishments },
      });

      expect(
        await screen.findByRole("heading", {
          level: 2,
          name: "Your profile doesn't include any screens yet",
        }),
      ).toBeVisible();
      expect(
        screen.getByText(
          "Ask an administrator in your organization to add permissions to your profile.",
        ),
      ).toBeVisible();
      expect(screen.queryByRole("navigation", { name: "Main" })).toBeNull();
      expect(called(fetch, "/profiles")).toBe(false);
      expect(called(fetch, "/ai/conversations")).toBe(false);
      await expectNoAxeViolations();
    },
  );

  it("explains establishments are needed, and never lists them, without establishments.read", async () => {
    const { fetch } = renderApp({
      "/me": { body: without("establishments.read") },
      "/establishments": { body: establishments },
    });

    expect(
      await screen.findByRole("heading", { name: "You don't have access to establishments" }),
    ).toBeVisible();
    expect(screen.queryByRole("combobox", { name: "Establishment" })).toBeNull();
    expect(called(fetch, "/establishments")).toBe(false);
    await expectNoAxeViolations();
  });

  it("reads a 403 from /establishments as a lost permission, not a missing organization", async () => {
    const { fetch } = renderApp({
      "/me": [{ body: me }, { body: without("establishments.read") }],
      "/establishments": { status: 403, body: { code: "permission.denied" } },
    });

    expect(
      await screen.findByRole("heading", { name: "You don't have access to establishments" }),
    ).toBeVisible();
    expect(screen.queryByText("Your account isn't linked to an organization")).toBeNull();
    expect(fetch.mock.calls.filter(([input]) => input === "/api/me")).toHaveLength(2);
  });

  it("lists the user's profile names in the account menu", async () => {
    renderApp({
      "/me": {
        body: {
          ...me,
          profiles: [
            { id: "p-manager", name: "Manager", systemKey: "manager" },
            { id: "p-kitchen", name: "Kitchen lead", systemKey: null },
          ],
        },
      },
      "/establishments": { body: establishments },
    });

    await userEvent.click(
      await screen.findByRole("button", { name: "Account menu, Olivia Owner" }),
    );

    expect(await screen.findByRole("menu")).toHaveTextContent("Manager, Kitchen lead");
  });
});
