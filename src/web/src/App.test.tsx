import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { App } from "./App";
import { createAuth } from "./auth/session";
import { establishments, expectNoAxeViolations, fakeOidc, me, stubApi } from "./test/fakes";

const storageKey = `normetry.establishment.${me.tenantId}.${me.userId}`;
const never = () => new Promise<Response>(() => {});

function renderApp(routes: Parameters<typeof stubApi>[0], user = fakeOidc()) {
  const fetch = stubApi(routes);
  const auth = createAuth(user.oidc);
  render(<App auth={auth} />);
  return { fetch, auth, oidc: user.oidc };
}

const picker = () => screen.getByRole("region", { name: "Establishments" });
const switcherNone = () => screen.getAllByRole("button", { name: "No establishment" })[0];

describe("auth screens", () => {
  it("shows a status row while redirecting to sign in", async () => {
    renderApp({}, fakeOidc(null));

    expect(await screen.findByRole("status")).toHaveTextContent("Redirecting to sign in…");
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
  it("shows skeleton rows while loading", async () => {
    renderApp({ "/me": never, "/establishments": never });

    const card = await screen.findByRole("region", { name: "Establishments" });
    expect(card).toHaveAttribute("aria-busy", "true");
    expect(within(card).getByText("Loading establishments…")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Loading establishments…" })).toHaveAttribute(
      "aria-disabled",
      "true",
    );
    expect(screen.getByText("Welcome back, Olivia")).toBeVisible();
    await expectNoAxeViolations();
  });

  it("lists the establishments with their type, jurisdiction and regulation", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });

    const row = await screen.findByRole("button", {
      name: "Bistro Lisboa, Restaurant, European Union",
    });
    expect(row).toHaveTextContent("Restaurant · European Union · Regulation (EC) 852/2004");
    expect(
      screen.getByRole("button", { name: "Bistro São Paulo, Restaurant, Brazil" }),
    ).toHaveTextContent("Anvisa RDC 216/2004");
    expect(picker()).toHaveAttribute("aria-busy", "false");
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

    expect(await screen.findByRole("button", { name: "Lab, Laboratory, CA" })).toHaveTextContent(
      "Laboratory · CA",
    );
  });

  it("selects on activation, remembers the id and moves focus to the chat region", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });

    await userEvent.click(
      await screen.findByRole("button", { name: "Bistro São Paulo, Restaurant, Brazil" }),
    );

    const heading = screen.getByRole("heading", { level: 2, name: "Bistro São Paulo" });
    expect(heading).toHaveFocus();
    expect(screen.getByRole("region", { name: "Bistro São Paulo" })).toHaveTextContent(
      "Questions about this establishment's regulation will appear here.",
    );
    expect(localStorage.getItem(storageKey)).toBe("e-sao-paulo");
    await expectNoAxeViolations();
  });

  it("restores the remembered establishment", async () => {
    localStorage.setItem(storageKey, "e-sao-paulo");
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });

    expect(
      await screen.findByRole("heading", { level: 2, name: "Bistro São Paulo" }),
    ).not.toHaveFocus();
    expect(
      screen.getByRole("combobox", {
        name: "Establishment: Bistro São Paulo. Change establishment",
      }),
    ).toBeVisible();
  });

  it("clears a remembered id that is no longer in the list", async () => {
    localStorage.setItem(storageKey, "e-gone");
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });

    await screen.findByRole("button", { name: "Bistro Lisboa, Restaurant, European Union" });
    await waitFor(() => expect(localStorage.getItem(storageKey)).toBeNull());
  });

  it("selects the only establishment automatically", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: [establishments[0]] } });

    expect(await screen.findByRole("heading", { level: 2, name: "Bistro Lisboa" })).toBeVisible();
  });

  it("shows the empty state", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: [] } });

    expect(await screen.findByRole("heading", { name: "No establishments yet" })).toBeVisible();
    expect(switcherNone()).toBeDisabled();
    await expectNoAxeViolations();
  });

  it("shows the error state and recovers with Try again", async () => {
    renderApp({
      "/me": { body: me },
      "/establishments": [{ status: 503 }, { status: 503 }, { body: establishments }],
    });

    const alert = await screen.findByRole("alert", {}, { timeout: 3000 });
    expect(alert).toHaveTextContent("Couldn't load establishments");
    expect(switcherNone()).toBeDisabled();
    await expectNoAxeViolations();

    await userEvent.click(within(alert).getByRole("button", { name: "Try again" }));

    expect(
      await screen.findByRole("button", { name: "Bistro Lisboa, Restaurant, European Union" }),
    ).toBeVisible();
  });

  it("explains a missing organization on 403", async () => {
    renderApp({ "/me": { status: 403 }, "/establishments": { status: 403 } });

    expect(
      await screen.findByRole("heading", {
        name: "Your account isn't linked to an organization",
      }),
    ).toBeVisible();
    expect(within(picker()).queryByRole("button")).toBeNull();
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

    await screen.findByRole("button", { name: "Bistro Lisboa, Restaurant, European Union" });
    expect(screen.queryByText("Organization:")).toBeNull();
  });

  it("has the landmarks, the skip link and the active nav item", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });

    await screen.findByRole("button", { name: "Bistro Lisboa, Restaurant, European Union" });
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

    const switcher = await screen.findByRole("combobox", {
      name: "Establishment: Bistro Lisboa. Change establishment",
    });
    expect(switcher).toHaveAttribute("aria-haspopup", "listbox");
    switcher.focus();
    await user.keyboard("{Enter}");

    const listbox = await screen.findByRole("listbox");
    expect(within(listbox).getByRole("option", { name: "Bistro Lisboa" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    await expectNoAxeViolations({ popoverOpen: true });
    await user.keyboard("{ArrowDown}{Enter}");

    await waitFor(() => expect(screen.queryByRole("listbox")).toBeNull());
    expect(screen.getByText("Bistro São Paulo selected")).toHaveAttribute("aria-live", "polite");
    expect(
      screen.getByRole("combobox", {
        name: "Establishment: Bistro São Paulo. Change establishment",
      }),
    ).toHaveFocus();
    expect(screen.getByRole("heading", { level: 2, name: "Bistro São Paulo" })).toBeVisible();
    expect(localStorage.getItem(storageKey)).toBe("e-sao-paulo");
  });

  it("closes the drawer after a choice in it and focuses the chat region", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });
    const user = userEvent.setup();
    await screen.findByRole("button", { name: "Bistro Lisboa, Restaurant, European Union" });

    await user.click(screen.getByRole("button", { name: "Open navigation" }));
    const drawer = await screen.findByRole("dialog", { name: "Navigation" });
    expect(within(drawer).getByRole("button", { name: "Close navigation" })).toBeVisible();
    await expectNoAxeViolations();
    within(drawer).getByRole("combobox", { name: "Choose establishment" }).focus();
    await user.keyboard("{Enter}");
    await screen.findByRole("listbox");
    await user.keyboard("{Enter}");

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus(),
    );
  });

  it("returns focus to the menu button when the drawer is dismissed", async () => {
    renderApp({ "/me": { body: me }, "/establishments": { body: establishments } });
    const user = userEvent.setup();
    await screen.findByRole("button", { name: "Bistro Lisboa, Restaurant, European Union" });

    await user.click(screen.getByRole("button", { name: "Open navigation" }));
    await screen.findByRole("dialog", { name: "Navigation" });
    await user.keyboard("{Escape}");

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Open navigation" })).toHaveFocus(),
    );
  });

  it("signs out from the account menu", async () => {
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
  });
});
