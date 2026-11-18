import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "../App";
import { createAuth } from "../auth/session";
import { dateTime } from "../copy";
import {
  allPermissions,
  establishments,
  expectNoAxeViolations,
  fakeFinding,
  fakeOidc,
  me,
  mockViewport,
  stubApi,
} from "../test/fakes";
import type { InspectionChange } from "./inspections";

// A hub per connection the screen opens; the test plays the server's side.
const hubs = vi.hoisted(() => {
  type Callback = () => void;
  class FakeHub {
    state = "Disconnected";
    events = new Map<string, (change: unknown) => void>();
    reconnecting: Callback = () => {};
    reconnected: Callback = () => {};
    closed: Callback = () => {};
    invoke = vi.fn(() => Promise.resolve());
    start = vi.fn(() => {
      this.state = "Connected";
      return Promise.resolve();
    });
    stop = vi.fn(() => {
      this.state = "Disconnected";
      return Promise.resolve();
    });
    on(name: string, handler: (change: unknown) => void) {
      this.events.set(name, handler);
    }
    onreconnecting(callback: Callback) {
      this.reconnecting = callback;
    }
    onreconnected(callback: Callback) {
      this.reconnected = callback;
    }
    onclose(callback: Callback) {
      this.closed = callback;
    }
  }
  return { FakeHub, all: [] as FakeHub[], urls: [] as unknown[] };
});

vi.mock("@microsoft/signalr", () => ({
  HttpTransportType: { WebSockets: 1 },
  HubConnectionState: { Connected: "Connected" },
  LogLevel: { Warning: 3 },
  HubConnectionBuilder: class {
    withUrl(url: string, options: unknown) {
      hubs.urls.push({ url, options });
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      const hub = new hubs.FakeHub();
      hubs.all.push(hub);
      return hub;
    }
  },
}));

beforeEach(() => {
  hubs.all.length = 0;
  hubs.urls.length = 0;
});

const latestHub = () => hubs.all[hubs.all.length - 1];
const joined = () => waitFor(() => expect(latestHub().invoke).toHaveBeenCalled());
const push = (change: Partial<InspectionChange>) =>
  act(() =>
    latestHub().events.get("InspectionChanged")?.({
      establishmentId: "e-lisboa",
      itemsEvaluated: 0,
      itemsTotal: 0,
      themes: [],
      failureReasonCode: null,
      ...change,
    }),
  );

const never = () => new Promise<Response>(() => {});
/** A route answering whatever `get()` holds at the time, so a test can change the server. */
const live = (get: () => unknown) => () => Promise.resolve(Response.json(get()));
const storageKey = `normetry.establishment.${me.tenantId}.${me.userId}`;
const LIST = "/establishments/e-lisboa/inspections";
const regulation = "Regulation (EC) 852/2004";

const summary = (id: string, status: string, requestedAt: string, extra: object = {}) => ({
  id,
  status,
  regulation,
  requestedAt,
  startedAt: null,
  completedAt: null,
  itemsTotal: 0,
  itemsEvaluated: 0,
  verdictCounts: { compliant: 0, nonCompliant: 0, insufficientEvidence: 0 },
  failureReasonCode: null,
  ...extra,
});

const runningRow = summary("i-run", "Running", "2026-11-17T10:42:00Z", {
  itemsTotal: 43,
  itemsEvaluated: 27,
});
const doneRow = summary("i-done", "Completed", "2026-11-16T16:05:00Z", {
  itemsTotal: 43,
  itemsEvaluated: 43,
  verdictCounts: { compliant: 33, nonCompliant: 6, insufficientEvidence: 4 },
});
const failedRow = summary("i-failed", "Failed", "2026-11-16T09:12:00Z", {
  failureReasonCode: "budget_exceeded",
});
const oddRow = summary("i-odd", "Failed", "2026-11-15T09:00:00Z", {
  failureReasonCode: "something_new",
});
const history = [runningRow, doneRow, failedRow, oddRow];

const themes = [
  { theme: "waste", itemsTotal: 0, itemsEvaluated: 0 },
  { theme: "pest_control", itemsTotal: 4, itemsEvaluated: 0 },
  { theme: "temperature_control", itemsTotal: 7, itemsEvaluated: 4 },
  { theme: "personal_hygiene", itemsTotal: 6, itemsEvaluated: 6 },
];
// The row's verdict counts come along; the page ignores them.
const detail = (row: ReturnType<typeof summary>, extra: object = {}) => ({
  ...row,
  establishmentId: "e-lisboa",
  themes: [],
  findings: [],
  ...extra,
});
const running = detail(runningRow, { themes });

function renderInspections(
  routes: Parameters<typeof stubApi>[0] = {},
  { path = "/inspections", width = 1280, permissions = allPermissions, remembered = true } = {},
) {
  mockViewport(width);
  if (remembered) localStorage.setItem(storageKey, "e-lisboa");
  window.history.replaceState(null, "", path);
  const fetch = stubApi({
    "/me": { body: { ...me, permissions } },
    "/establishments": { body: establishments },
    [LIST]: { body: history },
    [`${LIST}/i-run`]: { body: running },
    ...routes,
  });
  render(<App auth={createAuth(fakeOidc().oidc)} />);
  return fetch;
}

const card = () => screen.getByRole("region", { name: "Bistro Lisboa" });
const rows = () => within(card()).getAllByRole("row").slice(1);
const runButton = () => screen.findByRole("button", { name: /^(Run inspection|Starting…)$/ });
const politeRegion = () => document.querySelector('div[aria-live="polite"]');
const calls = (fetch: ReturnType<typeof stubApi>, path: string, method = "GET") =>
  fetch.mock.calls.filter(
    ([input, init]) => input === `/api${path}` && (init?.method ?? "GET") === method,
  );
/** The page's back link, which shares its name with the nav item. */
const backLink = () =>
  within(screen.getByRole("main")).getByRole("link", { name: "Simulated Inspection" });
const themeRows = () =>
  within(screen.getByRole("list", { name: "Themes" }))
    .getAllByRole("listitem")
    .map((item) => item.textContent);
const titleHeading = () => screen.getByRole("heading", { level: 2, name: /^Inspection of / });

describe("inspection history", () => {
  it("lists the inspections, newest first, with their status and results", async () => {
    renderInspections();

    const link = await screen.findByRole("link", { name: dateTime(runningRow.requestedAt) });
    expect(link).toHaveAttribute("href", "/inspections/i-run");
    expect(link).toHaveAccessibleDescription("Running 27 of 43 checked");
    expect(within(card()).getByText("4 inspections")).toBeVisible();
    expect(rows().map((row) => row.textContent)).toEqual([
      expect.stringMatching(/Running 27 of 43 checked—No results$/),
      expect.stringMatching(/Completed6 non-compliant · 4 insufficient evidence · 33 compliant$/),
      expect.stringMatching(/Failed Processing limit reached—No results$/),
      expect.stringMatching(/Failed Couldn't finish—No results$/),
    ]);
    expect(
      within(card()).getByRole("table", { name: "Inspections of Bistro Lisboa" }),
    ).toBeVisible();
    expect(screen.getByRole("heading", { level: 1, name: "Simulated Inspection" })).toBeVisible();
    expect(document.title).toBe("Simulated Inspection · Normetry");
    const nav = screen.getByRole("navigation", { name: "Main" });
    expect(
      within(nav)
        .getAllByRole("link")
        .map((item) => item.textContent),
    ).toEqual(["Ask the Regulation", "Documents", "Simulated Inspection", "Settings"]);
    expect(within(nav).getByRole("link", { name: "Simulated Inspection" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(hubs.urls).toMatchObject([
      { url: "/hubs/inspections", options: { skipNegotiation: true } },
    ]);
    await waitFor(() =>
      expect(latestHub().invoke).toHaveBeenCalledWith("JoinEstablishment", "e-lisboa"),
    );
    await expectNoAxeViolations();
  });

  it("holds Run while one is running, and View progress opens it", async () => {
    renderInspections();
    const run = await runButton();
    await screen.findByText("An inspection is running.");

    expect(run).toHaveAttribute("aria-disabled", "true");
    expect(run).toHaveAccessibleDescription("You can run another one when it finishes.");
    await userEvent.click(run);
    expect(window.location.pathname).toBe("/inspections");

    await userEvent.click(screen.getByRole("link", { name: "View progress" }));
    await waitFor(() => expect(window.location.pathname).toBe("/inspections/i-run"));
    await waitFor(() => expect(titleHeading()).toHaveFocus());
  });

  it("shows skeleton rows while loading", async () => {
    renderInspections({ [LIST]: never });

    await waitFor(() => expect(card()).toHaveAttribute("aria-busy", "true"));
    expect(within(card()).getByText("Loading inspections…")).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("invites a run in the empty state, only for who may run", async () => {
    renderInspections({ [LIST]: { body: [] } });

    expect(
      await screen.findByText(
        "Run an inspection to check Bistro Lisboa's documents against Regulation (EC) 852/2004. It takes a few minutes.",
      ),
    ).toBeVisible();
    expect(within(card()).getByText("0 inspections")).toBeVisible();
    expect(await runButton()).not.toHaveAttribute("aria-disabled", "true");
    await expectNoAxeViolations();
  });

  it("shows no Run inspection without inspections.run", async () => {
    renderInspections(
      { [LIST]: { body: [] } },
      { permissions: ["establishments.read", "inspections.read"] },
    );

    expect(await screen.findByText("Inspections of Bistro Lisboa appear here.")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Run inspection" })).toBeNull();
    await expectNoAxeViolations();
  });

  it("keeps the running notice from read-only users", async () => {
    renderInspections({}, { permissions: ["establishments.read", "inspections.read"] });

    await screen.findByRole("link", { name: dateTime(runningRow.requestedAt) });
    expect(screen.queryByText("An inspection is running.")).toBeNull();
  });

  it("shows a load error and recovers with Try again", async () => {
    renderInspections({ [LIST]: [{ status: 500 }, { status: 500 }, { body: history }] });

    // One retry, a second later, before the error shows.
    const retry = await screen.findByRole("button", { name: "Try again" }, { timeout: 3000 });
    expect(within(card()).getByRole("alert")).toHaveTextContent("Couldn't load inspections");
    await expectNoAxeViolations();
    await userEvent.click(retry);

    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus(),
    );
    expect(rows()).toHaveLength(4);
  });

  it("is laid out for 375px without violations", async () => {
    renderInspections({}, { width: 375 });

    await screen.findByRole("link", { name: dateTime(runningRow.requestedAt) });
    await expectNoAxeViolations();
  });
});

describe("run inspection", () => {
  const idle = [doneRow, failedRow];

  it("starts one, opens it waiting, and announces it", async () => {
    const requested = detail(summary("i-new", "Requested", "2026-11-17T11:00:00Z"));
    let server: unknown[] = idle;
    const fetch = renderInspections({
      [LIST]: live(() => server),
      [`POST ${LIST}`]: () => {
        server = [summary("i-new", "Requested", requested.requestedAt), ...idle];
        return Promise.resolve(Response.json({ id: "i-new" }, { status: 202 }));
      },
      [`${LIST}/i-new`]: { body: requested },
    });
    const run = await runButton();
    await screen.findByRole("link", { name: dateTime(doneRow.requestedAt) });

    await userEvent.click(run);

    await waitFor(() => expect(window.location.pathname).toBe("/inspections/i-new"));
    await waitFor(() => expect(titleHeading()).toHaveFocus());
    expect(politeRegion()).toHaveTextContent("Inspection started.");
    expect(await screen.findByText("Waiting to start…")).toBeVisible();
    expect(screen.getByText("The inspection is queued and starts in a moment.")).toBeVisible();
    expect(screen.getByText(`${regulation} · Bistro Lisboa`)).toBeVisible();
    expect(
      within(screen.getByRole("list", { name: "Inspection steps" })).getByText("Analysis")
        .parentElement,
    ).toHaveAttribute("aria-current", "step");
    // No total yet: the ring is indeterminate and reads the state.
    const ring = screen.getByRole("progressbar", { name: "Requirements checked" });
    expect(ring).not.toHaveAttribute("aria-valuenow");
    expect(ring).toHaveAttribute("aria-valuetext", "Waiting to start…");
    expect(screen.queryByRole("list", { name: "Themes" })).toBeNull();
    await waitFor(() =>
      expect(document.title).toBe(
        `Inspection of ${dateTime(requested.requestedAt)} · Simulated Inspection · Normetry`,
      ),
    );
    expect(calls(fetch, LIST, "POST")).toHaveLength(1);
    await expectNoAxeViolations();

    await userEvent.click(backLink());
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus(),
    );
    expect(await screen.findByText("An inspection is running.")).toBeVisible();
  });

  it("holds Run after the daily quota, saying when it resets", async () => {
    renderInspections({
      [LIST]: { body: idle },
      [`POST ${LIST}`]: {
        status: 429,
        body: { code: "inspection_quota" },
        headers: { "Retry-After": String(6 * 3600) },
      },
    });
    const run = await runButton();

    await userEvent.click(run);

    expect(
      await screen.findByText("Your organization has run all of today's inspections."),
    ).toBeVisible();
    expect(run).toHaveAttribute("aria-disabled", "true");
    expect(run).toHaveAccessibleDescription("You can run another inspection in 6 hours.");
    expect(
      screen.getByText("You can run another inspection in 6 hours.").closest("[role]"),
    ).toHaveAttribute("role", "status");
    await expectNoAxeViolations();
  });

  it("holds Run until a rate limit passes, then clears the notice", async () => {
    renderInspections({
      [LIST]: { body: idle },
      [`POST ${LIST}`]: { status: 429, headers: { "Retry-After": "1" } },
    });
    const run = await runButton();

    await userEvent.click(run);

    expect(await screen.findByText("Too many requests right now.")).toBeVisible();
    expect(run).toHaveAccessibleDescription("Try again in 1 minute.");
    await waitFor(() => expect(screen.queryByText("Too many requests right now.")).toBeNull(), {
      timeout: 3000,
    });
    expect(run).not.toHaveAttribute("aria-disabled", "true");
  });

  it("points to Documents when there is nothing indexed to inspect", async () => {
    renderInspections({
      [LIST]: { body: idle },
      [`POST ${LIST}`]: { status: 409, body: { code: "no_indexed_documents" } },
      "/establishments/e-lisboa/documents": { body: [] },
    });
    const run = await runButton();

    await userEvent.click(run);

    expect(await screen.findByText("Bistro Lisboa has no indexed documents yet.")).toBeVisible();
    expect(run).not.toHaveAttribute("aria-disabled", "true");
    const documents = screen.getByRole("link", { name: "Go to Documents" });
    expect(documents).toHaveAttribute("href", "/documents");
    await expectNoAxeViolations();

    await userEvent.click(documents);

    await waitFor(() => expect(window.location.pathname).toBe("/documents"));
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus(),
    );
    expect(screen.getByRole("heading", { level: 1, name: "Documents" })).toBeVisible();
  });

  it("says when the organization's other inspections fill its capacity", async () => {
    renderInspections({
      [LIST]: { body: idle },
      [`POST ${LIST}`]: { status: 409, body: { code: "inspection_capacity" } },
    });
    const run = await runButton();

    await userEvent.click(run);

    expect(
      await screen.findByText(
        "Your organization is running as many inspections as it can at once.",
      ),
    ).toBeVisible();
    expect(
      screen.getByText(
        "You can run one here when an inspection of another establishment finishes.",
      ),
    ).toBeVisible();
    expect(
      screen
        .getByText("Your organization is running as many inspections as it can at once.")
        .closest("[role]"),
    ).toHaveAttribute("role", "status");
    expect(run).not.toHaveAttribute("aria-disabled", "true");
    expect(within(card()).queryByRole("link", { name: "View progress" })).toBeNull();
    await expectNoAxeViolations();
  });

  it("refetches a stale list when one is already running", async () => {
    let server: unknown[] = idle;
    renderInspections({
      [LIST]: live(() => server),
      [`POST ${LIST}`]: () => {
        server = history;
        return Promise.resolve(Response.json({ code: "inspection_running" }, { status: 409 }));
      },
    });
    const run = await runButton();
    await screen.findByRole("link", { name: dateTime(doneRow.requestedAt) });

    await userEvent.click(run);

    expect(await screen.findByText("An inspection is running.")).toBeVisible();
    expect(run).toHaveAttribute("aria-disabled", "true");
  });

  it("says when the permission was taken away, hides Run and keeps focus on the card", async () => {
    const fetch = renderInspections({
      "/me": [
        { body: me },
        { body: { ...me, permissions: allPermissions.filter((p) => p !== "inspections.run") } },
      ],
      [LIST]: { body: idle },
      [`POST ${LIST}`]: { status: 403, body: { code: "permission.denied" } },
    });

    await userEvent.click(await runButton());

    expect(await screen.findByText("You no longer have permission to do this.")).toBeVisible();
    await waitFor(() =>
      expect(screen.queryByRole("button", { name: "Run inspection" })).toBeNull(),
    );
    expect(calls(fetch, "/me")).toHaveLength(2);
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus(),
    );
  });

  it("says a start failed, and clears the notice on the next click", async () => {
    renderInspections({
      [LIST]: { body: idle },
      [`POST ${LIST}`]: [() => Promise.reject(new TypeError("Failed to fetch")), never],
    });
    const run = await runButton();

    await userEvent.click(run);
    expect(await screen.findByText("Couldn't start the inspection.")).toBeVisible();
    expect(run).not.toHaveAttribute("aria-disabled", "true");

    await userEvent.click(run);
    expect(screen.queryByText("Couldn't start the inspection.")).toBeNull();
    expect(run).toHaveTextContent("Starting…");
    expect(run).toHaveAttribute("aria-disabled", "true");
  });
});

describe("inspection page", () => {
  it("shows a running inspection's steps, progress and themes", async () => {
    renderInspections({}, { path: "/inspections/i-run" });

    expect(await screen.findByText("Inspection in progress…")).toBeVisible();
    expect(titleHeading()).toHaveTextContent(`Inspection of ${dateTime(runningRow.requestedAt)}`);
    expect(
      screen.getByText(`Checking Bistro Lisboa's documents against ${regulation}.`),
    ).toBeVisible();
    const ring = screen.getByRole("progressbar", { name: "Requirements checked" });
    expect(ring).toHaveAttribute("aria-valuetext", "27 of 43 requirements checked");
    expect(ring).toHaveTextContent("62%");
    expect(themeRows()).toEqual([
      "Done: Personal hygiene6 of 6",
      "In progress: Temperature control4 of 7",
      "Not started: Pest control0 of 4",
    ]);
    expect(
      screen.getByText("You can leave this page. The inspection keeps running."),
    ).toBeVisible();
    await waitFor(() =>
      expect(document.title).toBe(
        `Inspection of ${dateTime(runningRow.requestedAt)} · Simulated Inspection · Normetry`,
      ),
    );
    await expectNoAxeViolations();
  });

  it("opens from a row and goes back to the history, moving focus each way", async () => {
    renderInspections();

    await userEvent.click(
      await screen.findByRole("link", { name: dateTime(runningRow.requestedAt) }),
    );
    await waitFor(() => expect(titleHeading()).toHaveFocus());
    await screen.findByText("Inspection in progress…");

    await userEvent.click(backLink());
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus(),
    );
  });

  it("explains why an inspection failed", async () => {
    renderInspections(
      {
        [`${LIST}/i-failed`]: {
          body: detail(failedRow, { failureReasonCode: "timed_out" }),
        },
      },
      { path: "/inspections/i-failed" },
    );

    expect(
      await screen.findByRole("heading", {
        level: 3,
        name: "This inspection stopped before it finished.",
      }),
    ).toBeVisible();
    expect(
      screen.getByText("It took too long and was stopped. No findings were kept. Run it again."),
    ).toBeVisible();
    expect(screen.queryByRole("list", { name: "Inspection steps" })).toBeNull();
    await expectNoAxeViolations();
  });

  it("falls back to the generic reason for an unknown failure code", async () => {
    renderInspections(
      { [`${LIST}/i-odd`]: { body: detail(oddRow) } },
      { path: "/inspections/i-odd" },
    );

    expect(
      await screen.findByText(
        "It stopped before it finished. No findings were kept. Run it again.",
      ),
    ).toBeVisible();
  });

  it("shows skeletons while it loads, with the list's title when known", async () => {
    renderInspections({ [`${LIST}/i-run`]: never }, { path: "/inspections/i-run" });

    expect(await screen.findByText("Loading inspection…")).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("shows a load error and recovers with Try again", async () => {
    renderInspections(
      { [`${LIST}/i-run`]: [{ status: 500 }, { status: 500 }, { body: running }] },
      { path: "/inspections/i-run" },
    );

    const retry = await screen.findByRole("button", { name: "Try again" }, { timeout: 3000 });
    expect(screen.getByRole("alert")).toHaveTextContent("Couldn't load this inspection");
    await expectNoAxeViolations();
    await userEvent.click(retry);

    await waitFor(() => expect(titleHeading()).toHaveFocus());
  });

  it("shows another establishment's or a missing inspection as not found", async () => {
    renderInspections(
      { [`${LIST}/i-gone`]: { status: 404, body: { code: "inspection.not_found" } } },
      { path: "/inspections/i-gone" },
    );

    expect(
      await screen.findByRole("heading", { level: 2, name: "This inspection isn't available" }),
    ).toBeVisible();
    expect(
      screen.getByText("It may belong to another establishment, or it no longer exists."),
    ).toBeVisible();
    await expectNoAxeViolations();
    await userEvent.click(screen.getByRole("link", { name: "Back to inspections" }));

    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus(),
    );
  });

  it("is laid out for 375px without violations", async () => {
    renderInspections({}, { path: "/inspections/i-run", width: 375 });

    await screen.findByText("Inspection in progress…");
    await expectNoAxeViolations();
  });
});

describe("live progress", () => {
  it("patches the open inspection's progress and announces milestones once", async () => {
    const fetch = renderInspections({}, { path: "/inspections/i-run" });
    await screen.findByText("Inspection in progress…");
    await joined();
    const before = calls(fetch, `${LIST}/i-run`).length;
    const progress = (evaluated: number, temperature: number) =>
      push({
        inspectionId: "i-run",
        status: "Running",
        itemsEvaluated: evaluated,
        itemsTotal: 43,
        themes: themes.map((theme) =>
          theme.theme === "temperature_control" ? { ...theme, itemsEvaluated: temperature } : theme,
        ),
      });

    progress(33, 7);
    progress(34, 7);

    const ring = screen.getByRole("progressbar", { name: "Requirements checked" });
    await waitFor(() => expect(ring).toHaveAttribute("aria-valuenow", "34"));
    expect(ring).toHaveAttribute("aria-valuetext", "34 of 43 requirements checked");
    expect(themeRows()).toContain("Done: Temperature control7 of 7");
    expect(politeRegion()?.textContent).toBe("33 of 43 requirements checked.");
    expect(calls(fetch, `${LIST}/i-run`)).toHaveLength(before);

    progress(43, 7);
    expect(await screen.findByText("Preparing findings…")).toBeVisible();
    const steps = within(screen.getByRole("list", { name: "Inspection steps" }));
    expect(steps.getAllByRole("listitem")[0]).toHaveTextContent("Done: Analysis");
    expect(steps.getByText("Findings").parentElement).toHaveAttribute("aria-current", "step");
  });

  it("patches a history row, and refetches for one it doesn't list", async () => {
    const fetch = renderInspections();
    await screen.findByRole("link", { name: dateTime(runningRow.requestedAt) });
    await joined();
    await waitFor(() => expect(calls(fetch, LIST).length).toBeGreaterThan(0));
    const before = calls(fetch, LIST).length;

    push({ inspectionId: "i-run", status: "Running", itemsEvaluated: 30, itemsTotal: 43 });

    await waitFor(() =>
      expect(
        screen.getByRole("link", { name: dateTime(runningRow.requestedAt) }),
      ).toHaveAccessibleDescription("Running 30 of 43 checked"),
    );
    expect(calls(fetch, LIST)).toHaveLength(before);

    push({ inspectionId: "i-elsewhere", status: "Running", itemsEvaluated: 1, itemsTotal: 43 });
    await waitFor(() => expect(calls(fetch, LIST).length).toBeGreaterThan(before));
  });

  it("swaps to the results when the open inspection completes, and announces it", async () => {
    let server: object = running;
    let list: unknown[] = history;
    const fetch = renderInspections(
      { [`${LIST}/i-run`]: live(() => server), [LIST]: live(() => list) },
      { path: "/inspections/i-run" },
    );
    await screen.findByText("Inspection in progress…");
    await joined();
    const before = calls(fetch, `${LIST}/i-run`).length;

    const counts = { compliant: 1, nonCompliant: 1, insufficientEvidence: 0 };
    server = detail(runningRow, {
      status: "Completed",
      itemsEvaluated: 43,
      findings: [
        fakeFinding({ id: "f-1" }),
        fakeFinding({ id: "f-2", verdict: "compliant", title: "The water tank is cleaned" }),
      ],
    });
    list = [{ ...runningRow, status: "Completed", verdictCounts: counts }, doneRow];
    push({ inspectionId: "i-run", status: "Completed", itemsEvaluated: 43, itemsTotal: 43 });

    expect(await screen.findByRole("heading", { level: 2, name: "Findings" })).toBeVisible();
    expect(screen.getByText("2 findings · 2 pending review")).toBeVisible();
    expect(screen.queryByRole("progressbar")).toBeNull();
    expect(calls(fetch, `${LIST}/i-run`).length).toBeGreaterThan(before);
    await waitFor(() =>
      expect(politeRegion()).toHaveTextContent("Inspection complete: 2 findings."),
    );
    await waitFor(() => expect(titleHeading()).toHaveFocus());
  });

  it("swaps to the failure when the open inspection stops, and announces it", async () => {
    let server: object = running;
    renderInspections({ [`${LIST}/i-run`]: live(() => server) }, { path: "/inspections/i-run" });
    await screen.findByText("Inspection in progress…");
    await joined();

    server = detail(runningRow, { status: "Failed", failureReasonCode: "budget_exceeded" });
    push({
      inspectionId: "i-run",
      status: "Failed",
      itemsEvaluated: 27,
      itemsTotal: 43,
      failureReasonCode: "budget_exceeded",
    });

    expect(politeRegion()).toHaveTextContent("The inspection stopped: Processing limit reached.");
    expect(
      await screen.findByText(
        "It needed more AI processing than one inspection is allowed, so it stopped. No findings were kept. Run it again, and if it stops again, contact your administrator.",
      ),
    ).toBeVisible();
    // Nothing is live on a failed inspection.
    act(() => latestHub().closed());
    expect(screen.queryByText("Live updates stopped. Progress may be out of date.")).toBeNull();
  });

  it("refetches the open inspection after a reconnect", async () => {
    const fetch = renderInspections({}, { path: "/inspections/i-run" });
    await screen.findByText("Inspection in progress…");
    await joined();
    await waitFor(() => expect(calls(fetch, `${LIST}/i-run`).length).toBeGreaterThan(0));
    const before = calls(fetch, `${LIST}/i-run`).length;

    act(() => latestHub().reconnecting());
    act(() => latestHub().reconnected());

    await waitFor(() => expect(calls(fetch, `${LIST}/i-run`).length).toBeGreaterThan(before));
    expect(latestHub().invoke).toHaveBeenLastCalledWith("JoinEstablishment", "e-lisboa");
  });

  it("says inside the running card when updates stopped", async () => {
    renderInspections({}, { path: "/inspections/i-run" });
    await screen.findByText("Inspection in progress…");
    await joined();

    act(() => latestHub().closed());

    // The card is the stepper's parent; the notice is its first child, as on Documents.
    const runningCard = screen.getByRole("list", { name: "Inspection steps" }).parentElement!;
    const status = within(runningCard).getByRole("status");
    expect(runningCard.firstElementChild).toBe(status);
    expect(status).toHaveTextContent("Live updates stopped. Progress may be out of date.");
    expect(within(status).getByRole("button", { name: "Reconnect" })).toBeVisible();
    await expectNoAxeViolations();
  });

  it("says when updates stopped, and Reconnect starts over and refetches the list", async () => {
    const fetch = renderInspections();
    await screen.findByRole("link", { name: dateTime(runningRow.requestedAt) });
    await joined();

    act(() => latestHub().closed());
    const notice = screen.getByText("Live updates stopped. Progress may be out of date.");
    await expectNoAxeViolations();
    const before = calls(fetch, LIST).length;

    await userEvent.click(screen.getByRole("button", { name: "Reconnect" }));

    await waitFor(() => expect(notice).not.toBeInTheDocument());
    expect(hubs.all).toHaveLength(2);
    expect(latestHub().invoke).toHaveBeenCalledWith("JoinEstablishment", "e-lisboa");
    expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus();
    await waitFor(() => expect(calls(fetch, LIST).length).toBeGreaterThan(before));
  });
});

describe("inspections access and navigation", () => {
  it("refuses the screen without inspections.read and never asks for inspections", async () => {
    const fetch = renderInspections({}, { permissions: ["establishments.read", "chat.ask"] });

    expect(
      await screen.findByRole("heading", {
        level: 2,
        name: "You don't have access to Simulated Inspection",
      }),
    ).toBeVisible();
    expect(screen.queryByRole("link", { name: "Simulated Inspection" })).toBeNull();
    expect(fetch.mock.calls.some(([input]) => input.includes("/inspections"))).toBe(false);
    expect(hubs.all).toHaveLength(0);
    await expectNoAxeViolations();
  });

  it("names Simulated Inspection in the establishments refusal", async () => {
    renderInspections({}, { permissions: ["inspections.read"] });

    expect(
      await screen.findByText(
        "Documents, Simulated Inspection and Ask the Regulation work per establishment. Ask an administrator in your organization for access.",
      ),
    ).toBeVisible();
  });

  it("sends a user who may only inspect from home to Simulated Inspection", async () => {
    renderInspections({}, { path: "/", permissions: ["establishments.read", "inspections.read"] });

    expect(
      await screen.findByRole("link", { name: dateTime(runningRow.requestedAt) }),
    ).toBeVisible();
    expect(window.location.pathname).toBe("/inspections");
  });

  it("goes to the history when the establishment changes on an inspection", async () => {
    renderInspections(
      { "/establishments/e-sao-paulo/inspections": { body: [] } },
      { path: "/inspections/i-run" },
    );
    await screen.findByText("Inspection in progress…");
    const user = userEvent.setup();

    const switcher = screen.getByRole("combobox", { name: "Establishment" });
    switcher.focus();
    await user.keyboard("{Enter}");
    await screen.findByRole("listbox");
    await user.keyboard("{ArrowDown}{Enter}");

    await waitFor(() => expect(window.location.pathname).toBe("/inspections"));
    expect(await screen.findByRole("region", { name: "Bistro São Paulo" })).toBeVisible();
    await waitFor(() =>
      expect(latestHub().invoke).toHaveBeenLastCalledWith("JoinEstablishment", "e-sao-paulo"),
    );
    expect(hubs.all).toHaveLength(1);
  });
});
