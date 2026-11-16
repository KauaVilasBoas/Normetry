import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { App } from "../App";
import { createAuth } from "../auth/session";
import {
  allPermissions,
  establishments,
  expectNoAxeViolations,
  fakeOidc,
  me,
  mockViewport,
  stubApi,
} from "../test/fakes";

const never = () => new Promise<Response>(() => {});

const owner = { id: "p-owner", name: "Owner", systemKey: "owner", grantsAll: true };
const manager = { id: "p-manager", name: "Manager", systemKey: "manager", grantsAll: false };
const auditor = { id: "p-auditor", name: "Auditor", systemKey: "auditor", grantsAll: false };
const kitchen = { id: "p-kitchen", name: "Kitchen lead", systemKey: null, grantsAll: false };
const summary = ({ id, name, systemKey }: typeof kitchen | typeof owner) => ({
  id,
  name,
  systemKey,
});

// The server lists them by name; the screen puts system profiles first.
const profiles = [
  { ...auditor, permissionCount: 4 },
  { ...kitchen, permissionCount: 1 },
  { ...manager, permissionCount: 7 },
  { ...owner, permissionCount: 8 },
];

const users = [
  {
    id: "u-2",
    displayName: "Marco Costa",
    email: "marco.costa@example.com",
    isCurrentUser: false,
    profiles: [summary(kitchen), summary(manager)],
  },
  {
    id: "u-1",
    displayName: "Olivia Owner",
    email: "owner@bistro.example",
    isCurrentUser: true,
    profiles: [summary(owner)],
  },
  {
    id: "u-3",
    displayName: null,
    email: "new.cook@example.com",
    isCurrentUser: false,
    profiles: [],
  },
];

const readOnly = { ...me, permissions: ["establishments.read", "chat.ask", "profiles.read"] };

function renderSettings(
  routes: Parameters<typeof stubApi>[0] = {},
  { path = "/settings/profiles", width = 1280 } = {},
) {
  mockViewport(width);
  window.history.replaceState(null, "", path);
  const fetch = stubApi({
    "/me": { body: me },
    "/establishments": { body: establishments },
    "/permissions": { body: allPermissions.map((name) => ({ name })) },
    "/profiles": { body: profiles },
    "/profiles/p-owner": { body: { ...owner, permissions: allPermissions } },
    "/profiles/p-manager": { body: { ...manager, permissions: allPermissions.slice(0, 7) } },
    "/profiles/p-kitchen": { body: { ...kitchen, permissions: ["chat.ask"] } },
    "/users": { body: users },
    ...routes,
  });
  render(<App auth={createAuth(fakeOidc().oidc)} />);
  return fetch;
}

const listCard = () => screen.getByRole("region", { name: "Profiles" });
const editor = (name: string) => screen.findByRole("region", { name });
const usersCard = () => screen.findByRole("region", { name: "Users" });
/** The JSON body of the last request with this method. */
function sentJson(fetch: ReturnType<typeof stubApi>, method: string): unknown {
  const body = fetch.mock.calls.findLast(([, init]) => init?.method === method)?.[1]?.body;
  return typeof body === "string" ? JSON.parse(body) : undefined;
}

describe("profiles tab", () => {
  it("lists system profiles first and opens Owner, read-only, by default", async () => {
    renderSettings();

    const owners = await editor("Owner");
    expect(within(owners).getByText(/Owner has every permission/)).toBeVisible();
    expect(within(owners).getByText("View establishments").textContent).toBe(
      "Granted: View establishments",
    );
    expect(within(owners).queryByRole("checkbox")).toBeNull();
    expect(within(owners).queryByRole("button", { name: "Save changes" })).toBeNull();
    expect(within(owners).queryByRole("button", { name: "Rename profile" })).toBeNull();

    const rows = within(listCard()).getAllByRole("link");
    expect(rows.map((row) => row.textContent)).toEqual([
      "Owner System All permissions",
      "Manager System 7 permissions",
      "Auditor System 4 permissions",
      "Kitchen lead 1 permission",
    ]);
    expect(rows[0]).toHaveAttribute("aria-current", "page");

    expect(screen.getByRole("heading", { level: 1, name: "Settings" })).toBeVisible();
    expect(document.title).toBe("Profiles · Settings · Normetry");
    expect(
      within(screen.getByRole("navigation", { name: "Settings sections" })).getByRole("link", {
        name: "Profiles",
      }),
    ).toHaveAttribute("aria-current", "page");
    expect(
      within(screen.getByRole("navigation", { name: "Main" })).getByRole("link", {
        name: "Settings",
      }),
    ).toHaveAttribute("aria-current", "page");
    await within(await usersCard()).findByText("Marco Costa");
    await expectNoAxeViolations();
  });

  it("redirects /settings to the Profiles tab", async () => {
    renderSettings({}, { path: "/settings" });

    await editor("Owner");
    expect(window.location.pathname).toBe("/settings/profiles");
  });

  it("shows skeletons while loading", async () => {
    renderSettings({ "/profiles": never, "/users": never });

    await waitFor(() => expect(listCard()).toHaveAttribute("aria-busy", "true"));
    expect(within(listCard()).getByText("Loading profiles…")).toBeInTheDocument();
    expect(await usersCard()).toHaveAttribute("aria-busy", "true");
    expect(within(await usersCard()).getByText("Loading users…")).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("selects a profile and moves focus to the editor heading", async () => {
    renderSettings();
    await editor("Owner");

    await userEvent.click(within(listCard()).getByRole("link", { name: /^Manager/ }));

    const heading = await screen.findByRole("heading", { level: 2, name: "Manager" });
    expect(heading).toHaveFocus();
    expect(window.location.pathname).toBe("/settings/profiles/p-manager");
    const managers = await editor("Manager");
    expect(
      within(managers).getByText(
        "System profile. You can change its permissions, but you can't delete it.",
      ),
    ).toBeVisible();
    expect(await within(managers).findAllByRole("checkbox")).toHaveLength(8);
    expect(within(managers).queryByRole("button", { name: "Rename profile" })).toBeNull();
    expect(within(managers).queryByRole("button", { name: "Delete profile" })).toBeNull();
  });

  it("edits the draft with the read dependency and saves the whole set", async () => {
    const fetch = renderSettings(
      {
        "PUT /profiles/p-kitchen/permissions": {
          body: {
            ...kitchen,
            permissions: ["chat.ask", "documents.read", "documents.delete"],
          },
        },
      },
      { path: "/settings/profiles/p-kitchen" },
    );
    const card = await editor("Kitchen lead");
    const save = await within(card).findByRole("button", { name: "Save changes" });
    expect(save).toHaveAttribute("aria-disabled", "true");
    const upload = within(card).getByRole("checkbox", { name: "Upload documents" });
    expect(upload).toHaveAccessibleDescription("Upload documents and new versions.");

    await userEvent.click(upload);
    expect(within(card).getByRole("checkbox", { name: "View documents" })).toBeChecked();
    expect(within(card).getByText("Unsaved changes")).toBeVisible();
    await userEvent.click(within(card).getByRole("checkbox", { name: "View documents" }));
    expect(upload).not.toBeChecked();
    expect(within(card).queryByText("Unsaved changes")).toBeNull();
    await userEvent.click(within(card).getByRole("checkbox", { name: "Delete documents" }));
    await expectNoAxeViolations();

    await userEvent.click(save);

    expect(await screen.findByText("Kitchen lead saved.")).toBeInTheDocument();
    expect(sentJson(fetch, "PUT")).toEqual({
      permissions: ["chat.ask", "documents.delete", "documents.read"],
    });
    expect(within(card).queryByText("Unsaved changes")).toBeNull();
    expect(save).toHaveAttribute("aria-disabled", "true");
  });

  it("refetches /me after saving a profile you hold, and keeps focus when Save goes", async () => {
    const asManager = { ...me, profiles: [summary(manager)] };
    const fetch = renderSettings(
      {
        "/me": [{ body: asManager }, { body: { ...readOnly, profiles: [summary(manager)] } }],
        "PUT /profiles/p-manager/permissions": {
          body: { ...manager, permissions: allPermissions.slice(0, 6) },
        },
      },
      { path: "/settings/profiles/p-manager" },
    );
    const card = await editor("Manager");
    await userEvent.click(await within(card).findByRole("checkbox", { name: "View profiles" }));

    await userEvent.click(within(card).getByRole("button", { name: "Save changes" }));

    expect(
      await within(card).findByText("You can view profiles but not change them."),
    ).toBeVisible();
    expect(within(card).queryByRole("button", { name: "Save changes" })).toBeNull();
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Manager" })).toHaveFocus(),
    );
    expect(fetch.mock.calls.filter(([input]) => input === "/api/me")).toHaveLength(2);
  });

  it("keeps the draft and says so when a save fails", async () => {
    renderSettings(
      { "PUT /profiles/p-kitchen/permissions": { status: 500 } },
      { path: "/settings/profiles/p-kitchen" },
    );
    const card = await editor("Kitchen lead");
    await userEvent.click(await within(card).findByRole("checkbox", { name: "View profiles" }));

    await userEvent.click(within(card).getByRole("button", { name: "Save changes" }));

    expect(await within(card).findByRole("alert")).toHaveTextContent(
      "Couldn't save Kitchen lead. Try again.",
    );
    expect(within(card).getByRole("checkbox", { name: "View profiles" })).toBeChecked();
  });

  it("creates a profile, refusing an empty or taken name, and opens it", async () => {
    const fetch = renderSettings({
      "POST /profiles": [
        { status: 409, body: { code: "profile.name_taken" } },
        { status: 201, body: { id: "p-bar" } },
      ],
      "/profiles/p-bar": {
        body: {
          id: "p-bar",
          name: "Bar staff",
          systemKey: null,
          grantsAll: false,
          permissions: [],
        },
      },
    });
    await editor("Owner");
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "New profile" }));
    const dialog = await screen.findByRole("dialog", { name: "New profile" });
    const field = within(dialog).getByRole("textbox", { name: "Name" });
    expect(field).toHaveFocus();
    await user.click(within(dialog).getByRole("button", { name: "Create profile" }));
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field).toHaveAccessibleDescription("For example, Kitchen lead. Enter a name.");
    expect(field).toHaveFocus();

    await user.type(field, "Manager{Enter}");
    await waitFor(() =>
      expect(field).toHaveAccessibleDescription(
        "For example, Kitchen lead. A profile named “Manager” already exists.",
      ),
    );
    await expectNoAxeViolations();

    await user.clear(field);
    await user.type(field, "  Bar staff ");
    await user.click(within(dialog).getByRole("button", { name: "Create profile" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Bar staff" })).toHaveFocus(),
    );
    expect(window.location.pathname).toBe("/settings/profiles/p-bar");
    expect(sentJson(fetch, "POST")).toEqual({ name: "Bar staff" });
    expect(screen.getByText("“Bar staff” created. Choose its permissions.")).toBeInTheDocument();
    expect(await screen.findAllByRole("checkbox", { checked: false })).toHaveLength(8);
  });

  it("renames a custom profile and returns focus to Rename", async () => {
    const fetch = renderSettings(
      { "PATCH /profiles/p-kitchen": { status: 204 } },
      { path: "/settings/profiles/p-kitchen" },
    );
    const card = await editor("Kitchen lead");
    const user = userEvent.setup();

    const rename = within(card).getByRole("button", { name: "Rename profile" });
    await user.click(rename);
    const field = within(await screen.findByRole("dialog", { name: "Rename profile" })).getByRole(
      "textbox",
      { name: "Name" },
    );
    expect(field).toHaveValue("Kitchen lead");
    await user.clear(field);
    await user.type(field, "Head chef{Enter}");

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() => expect(rename).toHaveFocus());
    expect(sentJson(fetch, "PATCH")).toEqual({ name: "Head chef" });
    expect(screen.getByText("Profile renamed to “Head chef”.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Head chef" })).toBeVisible();
  });

  it("deletes a custom profile, then focuses the list", async () => {
    renderSettings(
      {
        "DELETE /profiles/p-kitchen": { status: 204 },
        "/profiles": [{ body: profiles }, { body: profiles.filter((p) => p.id !== kitchen.id) }],
      },
      { path: "/settings/profiles/p-kitchen" },
    );
    const card = await editor("Kitchen lead");
    const user = userEvent.setup();

    await user.click(within(card).getByRole("button", { name: "Delete profile" }));
    const dialog = await screen.findByRole("alertdialog", { name: "Delete “Kitchen lead”?" });
    expect(within(dialog).getByRole("button", { name: "Cancel" })).toHaveFocus();
    await expectNoAxeViolations();
    await user.click(within(dialog).getByRole("button", { name: "Delete" }));

    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Profiles" })).toHaveFocus(),
    );
    expect(window.location.pathname).toBe("/settings/profiles");
    expect(screen.getByText("“Kitchen lead” deleted.")).toBeInTheDocument();
    expect(within(listCard()).queryByRole("link", { name: /Kitchen lead/ })).toBeNull();
  });

  it("shows profiles read-only without profiles.manage", async () => {
    renderSettings({ "/me": { body: readOnly } }, { path: "/settings/profiles/p-kitchen" });

    const card = await editor("Kitchen lead");
    expect(
      await within(card).findByText("You can view profiles but not change them."),
    ).toBeVisible();
    expect(await within(card).findByText("Ask questions")).toBeVisible();
    expect(within(card).getByText("Ask questions").textContent).toBe("Granted: Ask questions");
    expect(within(card).getByText("View profiles").textContent).toBe("Not granted: View profiles");
    expect(within(card).queryByRole("checkbox")).toBeNull();
    expect(within(card).queryByRole("button")).toBeNull();
    expect(screen.queryByRole("button", { name: "New profile" })).toBeNull();
    await within(await usersCard()).findByText("Marco Costa");
    expect(within(await usersCard()).queryByRole("button")).toBeNull();
    await expectNoAxeViolations();
  });

  it("shows a profile that is gone as not found, with the way back", async () => {
    renderSettings({}, { path: "/settings/profiles/p-gone" });

    const card = await editor("This profile isn't available");
    expect(within(card).getByText("It may have been deleted.")).toBeVisible();
    await userEvent.click(within(card).getByRole("link", { name: "Back to profiles" }));

    expect(window.location.pathname).toBe("/settings/profiles");
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Profiles" })).toHaveFocus(),
    );
  });

  it("shows a load error in the list card and recovers with Try again", async () => {
    let answer: (response: Response) => void = () => {};
    const pending = new Promise<Response>((resolve) => {
      answer = resolve;
    });
    renderSettings({ "/profiles": [{ status: 503 }, { status: 503 }, () => pending] });

    const alert = await within(await screen.findByRole("region", { name: "Profiles" })).findByRole(
      "alert",
      {},
      { timeout: 3000 },
    );
    expect(alert).toHaveTextContent("Couldn't load profiles");
    await expectNoAxeViolations();
    const tryAgain = within(alert).getByRole("button", { name: "Try again" });
    await userEvent.click(tryAgain);
    await waitFor(() => expect(tryAgain).toHaveAttribute("aria-disabled", "true"));
    answer(Response.json(profiles));

    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Profiles" })).toHaveFocus(),
    );
    expect(await editor("Owner")).toBeVisible();
  });
});

describe("lost permissions and /me failures", () => {
  const meCalls = (fetch: ReturnType<typeof stubApi>) =>
    fetch.mock.calls.filter(([input]) => input === "/api/me").length;

  it("keeps a refusal in its dialog until it closes, then refetches /me and moves focus", async () => {
    const fetch = renderSettings({
      "/me": [{ body: me }, { body: readOnly }],
      "POST /profiles": { status: 403, body: { code: "permission.denied" } },
    });
    await editor("Owner");
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "New profile" }));
    const dialog = await screen.findByRole("dialog", { name: "New profile" });
    await user.type(within(dialog).getByRole("textbox", { name: "Name" }), "Bar staff{Enter}");

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "You no longer have permission to do this.",
    );
    expect(meCalls(fetch)).toBe(1);
    await expectNoAxeViolations();
    await user.keyboard("{Escape}");

    await waitFor(() => expect(screen.queryByRole("button", { name: "New profile" })).toBeNull());
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Owner" })).toHaveFocus(),
    );
    expect(meCalls(fetch)).toBe(2);
  });

  it("keeps a refusal in the profiles menu until it closes, then refetches /me", async () => {
    const fetch = renderSettings({
      "/me": [{ body: me }, { body: readOnly }],
      "PUT /users/u-2/profiles/p-auditor": { status: 403, body: { code: "permission.denied" } },
    });
    const user = userEvent.setup();
    await within(await usersCard()).findByText("Marco Costa");

    await user.click(screen.getByRole("button", { name: "Change profiles for Marco Costa" }));
    const menu = await screen.findByRole("menu");
    await user.click(within(menu).getByRole("menuitemcheckbox", { name: "Auditor" }));

    expect(
      await within(menu).findByText("You no longer have permission to do this."),
    ).toBeVisible();
    expect(meCalls(fetch)).toBe(1);
    await user.keyboard("{Escape}");

    await waitFor(() =>
      expect(screen.queryByRole("button", { name: /Change profiles/ })).toBeNull(),
    );
    expect(meCalls(fetch)).toBe(2);
  });

  it("shows a failed /me in the list card and recovers with Try again", async () => {
    renderSettings({ "/me": [{ status: 503 }, { status: 503 }, { body: me }] });

    const alert = await within(await screen.findByRole("region", { name: "Profiles" })).findByRole(
      "alert",
      {},
      { timeout: 3000 },
    );
    expect(alert).toHaveTextContent("Couldn't load profiles");
    expect(screen.queryByRole("region", { name: "Users" })).toBeNull();
    await expectNoAxeViolations();
    await userEvent.click(within(alert).getByRole("button", { name: "Try again" }));

    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Profiles" })).toHaveFocus(),
    );
    expect(await editor("Owner")).toBeVisible();
    expect(await usersCard()).toBeVisible();
  });

  it("explains a missing organization on a /me 403", async () => {
    renderSettings({ "/me": { status: 403 } });

    expect(
      await screen.findByRole("heading", {
        level: 2,
        name: "Your account isn't linked to an organization",
      }),
    ).toBeVisible();
    expect(screen.queryByRole("region", { name: "Profiles" })).toBeNull();
    await expectNoAxeViolations();
  });
});

describe("users", () => {
  it("lists users by name with their profiles", async () => {
    renderSettings();

    const card = await usersCard();
    await within(card).findByText("Marco Costa");
    const rows = within(card).getAllByRole("listitem");
    expect(rows.map((row) => row.querySelector("p")?.textContent)).toEqual([
      "Marco Costa",
      "new.cook@example.com",
      "Olivia Owner (you)",
    ]);
    expect(rows[0]).toHaveTextContent("marco.costa@example.comManagerKitchen lead");
    expect(rows[1]).toHaveTextContent("No profile");
    expect(within(rows[1]).getAllByText("new.cook@example.com")).toHaveLength(1);
  });

  it("assigns a profile from the menu, which stays open", async () => {
    const fetch = renderSettings({
      "PUT /users/u-2/profiles/p-auditor": { status: 204 },
      "/users": [
        { body: users },
        {
          body: users.map((user) =>
            user.id === "u-2" ? { ...user, profiles: [...user.profiles, summary(auditor)] } : user,
          ),
        },
      ],
    });
    const user = userEvent.setup();
    await within(await usersCard()).findByText("Marco Costa");

    await user.click(screen.getByRole("button", { name: "Change profiles for Marco Costa" }));
    const menu = await screen.findByRole("menu", { name: "Change profiles for Marco Costa" });
    expect(menu).toHaveTextContent("Profiles for Marco Costa");
    const auditors = within(menu).getByRole("menuitemcheckbox", { name: "Auditor" });
    expect(auditors).toHaveAttribute("aria-checked", "false");
    expect(within(menu).getByRole("menuitemcheckbox", { name: "Manager" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
    await expectNoAxeViolations({ popoverOpen: true });
    await user.click(auditors);

    await waitFor(() => expect(auditors).toHaveAttribute("aria-checked", "true"));
    expect(screen.getByRole("menu")).toBeVisible();
    await waitFor(() => expect(auditors).not.toHaveAttribute("aria-disabled"));
    expect(fetch).toHaveBeenCalledWith(
      "/api/users/u-2/profiles/p-auditor",
      expect.objectContaining({ method: "PUT" }),
    );
  });

  it("refuses to remove the last Owner inside the menu and keeps it checked", async () => {
    renderSettings({
      "DELETE /users/u-1/profiles/p-owner": {
        status: 409,
        body: { code: "profile.last_owner" },
      },
    });
    const user = userEvent.setup();
    await within(await usersCard()).findByText("Olivia Owner (you)");

    await user.click(screen.getByRole("button", { name: "Change profiles for Olivia Owner" }));
    const menu = await screen.findByRole("menu");
    const owners = within(menu).getByRole("menuitemcheckbox", { name: "Owner" });
    await user.click(owners);

    const refusal = await within(menu).findByText(
      "Olivia Owner is the organization's last Owner. Give the Owner profile to someone else first.",
    );
    expect(refusal.parentElement).toHaveAttribute("aria-live", "assertive");
    expect(owners).toHaveAttribute("aria-checked", "true");
    await expectNoAxeViolations({ popoverOpen: true });

    await user.keyboard("{Escape}");
    await user.click(screen.getByRole("button", { name: "Change profiles for Olivia Owner" }));
    expect(within(await screen.findByRole("menu")).queryByText(/last Owner/)).toBeNull();
  });

  it("refetches /me after changing your own profiles and focuses Users when the menu goes", async () => {
    const fetch = renderSettings({
      "/me": [{ body: me }, { body: readOnly }],
      "PUT /users/u-1/profiles/p-auditor": { status: 204 },
    });
    const user = userEvent.setup();
    await within(await usersCard()).findByText("Olivia Owner (you)");

    await user.click(screen.getByRole("button", { name: "Change profiles for Olivia Owner" }));
    await user.click(
      within(await screen.findByRole("menu")).getByRole("menuitemcheckbox", { name: "Auditor" }),
    );

    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Users" })).toHaveFocus(),
    );
    expect(screen.queryByRole("menu")).toBeNull();
    expect(screen.queryByRole("button", { name: /Change profiles/ })).toBeNull();
    expect(fetch.mock.calls.filter(([input]) => input === "/api/me")).toHaveLength(2);
  });
});

describe("settings access and narrow layout", () => {
  it("refuses Settings without profiles.read and never asks for profiles", async () => {
    const fetch = renderSettings({
      "/me": { body: { ...me, permissions: ["establishments.read", "chat.ask"] } },
    });

    expect(
      await screen.findByRole("heading", { level: 2, name: "You don't have access to Settings" }),
    ).toBeVisible();
    expect(screen.queryByRole("link", { name: "Settings" })).toBeNull();
    expect(fetch.mock.calls.some(([input]) => input.startsWith("/api/profiles"))).toBe(false);
    await expectNoAxeViolations();
  });

  it("sends a user without chat.ask from home to Settings", async () => {
    renderSettings(
      { "/me": { body: { ...readOnly, permissions: ["profiles.read"] } } },
      { path: "/" },
    );

    expect(await editor("Owner")).toBeVisible();
    expect(window.location.pathname).toBe("/settings/profiles");
    expect(screen.queryByRole("link", { name: "Ask the Regulation" })).toBeNull();
  });

  it("keeps the user on Settings when the establishment changes", async () => {
    renderSettings();
    await editor("Owner");
    const user = userEvent.setup();

    const switcher = screen.getByRole("combobox", { name: "Establishment" });
    switcher.focus();
    await user.keyboard("{Enter}");
    await screen.findByRole("listbox");
    await user.keyboard("{ArrowDown}{Enter}");

    await waitFor(() => expect(screen.queryByRole("listbox")).toBeNull());
    expect(window.location.pathname).toBe("/settings/profiles");
    expect(screen.getByText("Bistro São Paulo selected")).toBeInTheDocument();
  });

  it("is master and detail by route below 768px", async () => {
    renderSettings({}, { width: 375 });

    await within(await usersCard()).findByText("Marco Costa");
    expect(screen.queryByRole("region", { name: "Owner" })).toBeNull();
    expect(within(listCard()).queryByRole("link", { current: "page" })).toBeNull();
    await expectNoAxeViolations();

    await userEvent.click(within(listCard()).getByRole("link", { name: /^Kitchen lead/ }));

    expect(await screen.findByRole("heading", { level: 2, name: "Kitchen lead" })).toHaveFocus();
    expect(screen.queryByRole("region", { name: "Users" })).toBeNull();
    await screen.findAllByRole("checkbox");
    await expectNoAxeViolations();
    expect(screen.queryByRole("navigation", { name: "Settings sections" })).toBeNull();
    await userEvent.click(screen.getByRole("link", { name: "Profiles" }));

    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Profiles" })).toHaveFocus(),
    );
    expect(screen.queryByRole("region", { name: "Kitchen lead" })).toBeNull();
  });
});
