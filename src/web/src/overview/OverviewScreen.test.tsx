import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { Overview } from "../api/overview";
import { App } from "../App";
import { createAuth } from "../auth/session";
import { shortDay } from "../copy";
import {
  allPermissions,
  dayFromToday,
  establishments,
  expectNoAxeViolations,
  fakeFinding,
  fakeOidc,
  me,
  mockViewport,
  stubApi,
} from "../test/fakes";

// Links open Documents and Simulated Inspection, whose hubs connect and stay quiet.
vi.mock("@microsoft/signalr", () => import("../test/signalr"));

const never = () => new Promise<Response>(() => {});
const storageKey = `normetry.establishment.${me.tenantId}.${me.userId}`;
const OVERVIEW = "/establishments/e-lisboa/overview";
const completedAt = "2026-11-16T16:05:00Z";
const readInspections = ["establishments.read", "inspections.read"];
const readDocuments = ["establishments.read", "documents.read"];

const latestInspection = {
  id: "i-done",
  completedAt,
  regulation: "Regulation (EC) 852/2004",
  compliance: 33 / 43,
  verdictCounts: { compliant: 33, nonCompliant: 6, insufficientEvidence: 4 },
  byTheme: [
    { theme: "personal_hygiene", compliant: 5, nonCompliant: 1, insufficientEvidence: 0 },
    { theme: "temperature_control", compliant: 3, nonCompliant: 3, insufficientEvidence: 1 },
    {
      theme: "cross_contamination_allergens",
      compliant: 4,
      nonCompliant: 0,
      insufficientEvidence: 1,
    },
    { theme: "cleaning_sanitizing", compliant: 5, nonCompliant: 0, insufficientEvidence: 0 },
    { theme: "pest_control", compliant: 2, nonCompliant: 2, insufficientEvidence: 1 },
    { theme: "water_premises", compliant: 14, nonCompliant: 0, insufficientEvidence: 1 },
  ],
  topRiskThemes: [
    { theme: "temperature_control", score: 9.5 },
    { theme: "pest_control", score: 4 },
    { theme: "personal_hygiene", score: 2 },
  ],
};

const series = [
  { inspectionId: "i-jul", completedAt: "2026-07-03T10:00:00Z", compliance: 0.29 },
  { inspectionId: "i-aug", completedAt: "2026-08-20T10:00:00Z", compliance: null },
  { inspectionId: "i-oct", completedAt: "2026-10-28T10:00:00Z", compliance: 0.68 },
  { inspectionId: "i-done", completedAt, compliance: 33 / 43 },
];

const documents = {
  expired: 2,
  expiringSoon: 3,
  list: [
    {
      id: "d-license",
      title: "Food business license",
      type: "License",
      expiresOn: dayFromToday(-8),
    },
    { id: "d-pest", title: "Pest control certificate", type: "Other", expiresOn: dayFromToday(10) },
  ],
};

const full: Overview = {
  latestInspection,
  openActionItems: 8,
  overdueActionItems: 3,
  pendingReviews: 12,
  series,
  documents,
};
const inspectionsOnly: Overview = { ...full, documents: undefined };

function renderOverview(
  routes: Parameters<typeof stubApi>[0] = {},
  { path = "/overview", width = 1280, permissions = allPermissions } = {},
) {
  mockViewport(width);
  localStorage.setItem(storageKey, "e-lisboa");
  window.history.replaceState(null, "", path);
  const fetch = stubApi({
    "/me": { body: { ...me, permissions } },
    "/establishments": { body: establishments },
    [OVERVIEW]: { body: full },
    ...routes,
  });
  render(<App auth={createAuth(fakeOidc().oidc)} />);
  return fetch;
}

const title = () => screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" });
/** A KPI card, from its label. */
const kpi = (label: string) => screen.getByText(label, { selector: "dt" }).parentElement!;
const kpiLabels = () => [...document.querySelectorAll("dt")].map((term) => term.textContent);
const region = (name: string) => screen.getByRole("region", { name });
const loaded = () => screen.findByRole("region", { name: "Expiring documents" });
const themesTable = () =>
  screen.getByRole("table", {
    name: `Results by theme of the inspection of ${shortDay(completedAt)}`,
  });

describe("home and navigation", () => {
  it.each([
    ["inspections.read", readInspections, "/overview"],
    ["documents.read", readDocuments, "/overview"],
    ["chat.ask only", ["establishments.read", "chat.ask"], "/ask"],
    ["profiles.read only", ["profiles.read"], "/settings/profiles"],
  ])("sends a user with %s from / to their home", async (_case, permissions, home) => {
    renderOverview({}, { path: "/", permissions });

    await waitFor(() => expect(window.location.pathname).toBe(home));
  });

  it("puts Overview first in the nav, marked current", async () => {
    renderOverview();
    await loaded();

    const nav = screen.getByRole("navigation", { name: "Main" });
    expect(within(nav).getAllByRole("link")[0]).toHaveTextContent("Overview");
    expect(within(nav).getByRole("link", { name: "Overview" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(screen.getByRole("heading", { level: 1, name: "Overview" })).toBeVisible();
    expect(document.title).toBe("Overview · Normetry");
  });
});

describe("overview", () => {
  it("shows the KPIs, the verdicts, the themes, the trend and the lists", async () => {
    renderOverview();
    await loaded();

    expect(
      screen.getByText(
        `Regulation (EC) 852/2004 · Results from the inspection of ${shortDay(completedAt)}`,
      ),
    ).toBeVisible();
    expect(screen.getByRole("link", { name: "View inspection" })).toHaveAttribute(
      "href",
      "/inspections/i-done",
    );
    expect(kpiLabels()).toEqual([
      "Compliance",
      "Open actions",
      "Findings to review",
      "Documents to renew",
    ]);
    // 33 of 43 is 76.7%: rounded down.
    expect(kpi("Compliance")).toHaveTextContent("76%8 ptsvs previous inspection");
    expect(
      within(kpi("Compliance")).getByText("Up 8 points from the previous inspection."),
    ).toHaveClass("sr-only");
    expect(kpi("Open actions")).toHaveTextContent("83 overdue");
    expect(kpi("Findings to review")).toHaveTextContent("12Waiting for a reviewer's decision");
    expect(kpi("Documents to renew")).toHaveTextContent("52 expired3 expiring in 30 days");

    const legend = screen.getByRole("list", { name: "Verdicts" });
    expect(
      within(legend)
        .getAllByRole("link")
        .map((link) => link.textContent),
    ).toEqual([
      "Compliant76%(33)33 findings",
      "Insufficient evidence9%(4)4 findings",
      "Non-compliant13%(6)6 findings",
    ]);
    expect(within(legend).getByRole("link", { name: /^Non-compliant/ })).toHaveAttribute(
      "href",
      "/inspections/i-done?verdict=non_compliant",
    );

    const themeRows = within(themesTable()).getAllByRole("row").slice(1);
    expect(themeRows).toHaveLength(6);
    const temperature = within(themesTable()).getByRole("link", { name: "Temperature control" });
    expect(temperature).toHaveAccessibleDescription("42%");
    expect(temperature).toHaveAttribute("href", "/inspections/i-done?theme=temperature_control");
    expect(themeRows[3]).toHaveTextContent(
      "Cleaning and sanitizing0 non-compliant · 0 insufficient evidence · 5 compliant500100%",
    );

    expect(
      screen.getByRole("img", {
        name: `Compliance across the last 4 completed inspections, from 29% on ${shortDay(series[0].completedAt)} to 76% on ${shortDay(completedAt)}.`,
      }),
    ).toBeVisible();
    expect(
      within(region("Compliance over time")).getByText("Last 4 completed inspections"),
    ).toBeVisible();

    const risks = within(region("Top risk themes")).getAllByRole("listitem");
    expect(risks.map((item) => item.textContent)).toEqual([
      "1Temperature control3 non-compliant · 1 insufficient evidence",
      "2Pest control2 non-compliant · 1 insufficient evidence",
      "3Personal hygiene1 non-compliant · 0 insufficient evidence",
    ]);

    const expiring = region("Expiring documents");
    expect(
      within(expiring).getByRole("link", { name: "Food business license" }),
    ).toHaveAccessibleDescription("Expired");
    expect(
      within(expiring).getByRole("link", { name: "Pest control certificate" }),
    ).toHaveAccessibleDescription("Expires in 10 days");
    expect(within(expiring).getByText("Other", { exact: false })).toBeVisible();
    // Five to renew, two listed.
    expect(within(expiring).getByRole("link", { name: "View all documents" })).toHaveAttribute(
      "href",
      "/documents",
    );
    await expectNoAxeViolations();
  });

  it("gives the chart's values as a table, newest first, a missing result included", async () => {
    renderOverview();
    await loaded();

    await userEvent.click(screen.getByText("Show the data"));

    const table = screen.getByRole("table", { name: "Compliance by inspection" });
    const rows = within(table).getAllByRole("row").slice(1);
    expect(rows.map((row) => row.textContent)).toEqual([
      `${shortDay(completedAt)}76%`,
      `${shortDay(series[2].completedAt)}68%`,
      `${shortDay(series[1].completedAt)}—No result`,
      // 0.29 * 100 is 28.999…: still 29%.
      `${shortDay(series[0].completedAt)}29%`,
    ]);
    expect(within(rows[0]).getByRole("link")).toHaveAttribute("href", "/inspections/i-done");
    expect(within(rows[2]).getByText("No result")).toHaveClass("sr-only");
    await expectNoAxeViolations();
  });

  it.each([
    [0.9, "14 pts", "Down 14 points from the previous inspection."],
    [33 / 43, "", "Same as the previous inspection"],
  ])("shows a change from %s", async (previous, shown, sentence) => {
    renderOverview({
      [OVERVIEW]: {
        body: {
          ...full,
          series: [
            { inspectionId: "i-before", completedAt: "2026-10-01T10:00:00Z", compliance: previous },
            series[3],
          ],
        },
      },
    });
    await loaded();

    expect(kpi("Compliance")).toHaveTextContent(`76%${shown}`);
    expect(within(kpi("Compliance")).getByText(sentence)).toBeInTheDocument();
  });

  it("shows a verdict with no findings as text, not a link", async () => {
    renderOverview({
      [OVERVIEW]: {
        body: {
          ...full,
          latestInspection: {
            ...latestInspection,
            verdictCounts: { compliant: 33, nonCompliant: 10, insufficientEvidence: 0 },
          },
        },
      },
    });
    await loaded();

    const legend = screen.getByRole("list", { name: "Verdicts" });
    expect(within(legend).getAllByRole("link")).toHaveLength(2);
    expect(within(legend).getAllByRole("listitem")[1]).toHaveTextContent(
      "Insufficient evidence0%(0)0 findings",
    );
    await expectNoAxeViolations();
  });

  it("shows only the inspection sections with inspections.read alone", async () => {
    renderOverview({ [OVERVIEW]: { body: inspectionsOnly } }, { permissions: readInspections });

    await screen.findByRole("region", { name: "Top risk themes" });
    expect(kpiLabels()).toEqual(["Compliance", "Open actions", "Findings to review"]);
    expect(screen.queryByRole("region", { name: "Expiring documents" })).toBeNull();
    expect(region("Compliance over time")).toHaveClass("xl:col-span-7");
    expect(region("Top risk themes")).toHaveClass("xl:col-span-5");
    await expectNoAxeViolations();
  });

  it("shows only the documents with documents.read alone", async () => {
    const fetch = renderOverview(
      { [OVERVIEW]: { body: { documents } } },
      { permissions: readDocuments },
    );
    await loaded();

    expect(kpiLabels()).toEqual(["Documents to renew"]);
    expect(screen.getByText("Regulation (EC) 852/2004")).toBeVisible();
    expect(screen.queryByRole("link", { name: "View inspection" })).toBeNull();
    expect(screen.queryByRole("region", { name: "Verdicts" })).toBeNull();
    expect(region("Expiring documents")).toHaveClass("xl:col-span-12");
    expect(fetch.mock.calls.some(([input]) => input.includes("/inspections"))).toBe(false);
    await expectNoAxeViolations();
  });

  it("invites a run before the first inspection, and its link focuses the history", async () => {
    renderOverview({
      [OVERVIEW]: {
        body: {
          latestInspection: null,
          openActionItems: 0,
          overdueActionItems: 0,
          pendingReviews: 0,
          series: [],
          documents,
        },
      },
      "/establishments/e-lisboa/inspections": { body: [] },
    });

    expect(
      await screen.findByRole("heading", { level: 3, name: "No inspection results yet" }),
    ).toBeVisible();
    expect(
      screen.getByText(
        "Run a simulated inspection to see how Bistro Lisboa complies with Regulation (EC) 852/2004. It takes a few minutes.",
      ),
    ).toBeVisible();
    expect(kpiLabels()).toEqual(["Documents to renew"]);
    expect(screen.queryByRole("region", { name: "Compliance over time" })).toBeNull();
    expect(region("Expiring documents")).toHaveClass("xl:col-span-12");
    await expectNoAxeViolations();

    await userEvent.click(screen.getByRole("link", { name: "Go to Simulated Inspection" }));
    await waitFor(() => expect(window.location.pathname).toBe("/inspections"));
    await waitFor(() => expect(title()).toHaveFocus());
  });

  it("says results will show, without inspections.run", async () => {
    renderOverview(
      {
        [OVERVIEW]: {
          body: {
            latestInspection: null,
            openActionItems: 0,
            overdueActionItems: 0,
            pendingReviews: 0,
            series: [],
          },
        },
      },
      { permissions: readInspections },
    );

    expect(
      await screen.findByText("Results show here once an inspection of Bistro Lisboa completes."),
    ).toBeVisible();
    expect(document.querySelector("dl")).toBeNull();
    await expectNoAxeViolations();
  });

  it("explains an inspection without results", async () => {
    renderOverview({
      [OVERVIEW]: {
        body: {
          ...full,
          latestInspection: {
            ...latestInspection,
            compliance: null,
            verdictCounts: { compliant: 0, nonCompliant: 0, insufficientEvidence: 0 },
            byTheme: [],
            topRiskThemes: [],
          },
          pendingReviews: 0,
          overdueActionItems: 0,
          series: [{ ...series[3], compliance: null }],
        },
      },
    });

    expect(
      await screen.findByRole("heading", { level: 3, name: "No results in the latest inspection" }),
    ).toBeVisible();
    expect(within(kpi("Compliance")).getByText("No result")).toHaveClass("sr-only");
    expect(kpi("Compliance")).toHaveTextContent(/^Compliance—No result$/);
    expect(kpi("Open actions")).toHaveTextContent("None overdue");
    expect(kpi("Findings to review")).toHaveTextContent("All findings reviewed");
    expect(screen.queryByRole("region", { name: "Verdicts" })).toBeNull();
    expect(screen.queryByRole("region", { name: "Top risk themes" })).toBeNull();
    expect(screen.getByRole("heading", { level: 3, name: "No trend yet" })).toBeVisible();
    await expectNoAxeViolations();
  });

  it("has no trend with one result, and nothing to compare", async () => {
    renderOverview({
      [OVERVIEW]: {
        body: {
          ...full,
          series: [series[3]],
          documents: { expired: 0, expiringSoon: 0, list: [] },
        },
      },
    });

    expect(await screen.findByRole("heading", { level: 3, name: "No trend yet" })).toBeVisible();
    expect(screen.queryByRole("img")).toBeNull();
    expect(screen.queryByText("Show the data")).toBeNull();
    expect(kpi("Compliance")).toHaveTextContent("No earlier inspection to compare");
    expect(kpi("Documents to renew")).toHaveTextContent("0Nothing expires in the next 30 days");
    expect(screen.getByRole("heading", { level: 3, name: "No documents to renew" })).toBeVisible();
    expect(screen.queryByRole("link", { name: "View all documents" })).toBeNull();
    await expectNoAxeViolations();
  });

  it("shows the skeleton while loading", async () => {
    renderOverview({ [OVERVIEW]: never });

    const loading = await screen.findByText("Loading overview…");
    expect(loading).toHaveClass("sr-only");
    expect(title().closest("[aria-busy]")).toHaveAttribute("aria-busy", "true");
    await expectNoAxeViolations();
  });

  it("shows the error, and Try again focuses the heading once it loads", async () => {
    renderOverview({ [OVERVIEW]: [{ status: 503 }, { status: 503 }, { body: full }] });

    const alert = await screen.findByRole("alert", {}, { timeout: 3000 });
    expect(alert).toHaveTextContent("Couldn't load the overview");
    await expectNoAxeViolations();
    await userEvent.click(within(alert).getByRole("button", { name: "Try again" }));

    await loaded();
    await waitFor(() => expect(title()).toHaveFocus());
  });

  it("shows the skeleton for another establishment, never the old numbers", async () => {
    renderOverview({ "/establishments/e-sao-paulo/overview": never });
    await loaded();
    const user = userEvent.setup();

    const switcher = screen.getByRole("combobox", { name: "Establishment" });
    switcher.focus();
    await user.keyboard("{Enter}");
    await screen.findByRole("listbox");
    await user.keyboard("{ArrowDown}{Enter}");

    expect(
      await screen.findByRole("heading", { level: 2, name: "Bistro São Paulo" }),
    ).toBeVisible();
    expect(window.location.pathname).toBe("/overview");
    expect(screen.getByText("Loading overview…")).toBeInTheDocument();
    expect(screen.queryByText("76%")).toBeNull();
  });

  it("opens a document and focuses its title", async () => {
    renderOverview({
      "/documents/d-license": {
        body: {
          id: "d-license",
          establishmentId: "e-lisboa",
          title: "Food business license",
          type: "License",
          createdAt: "2026-11-01T10:00:00Z",
          updatedAt: "2026-11-14T10:00:00Z",
          currentVersionNumber: null,
          expiresOn: documents.list[0].expiresOn,
          versions: [],
        },
      },
    });
    await loaded();

    await userEvent.click(screen.getByRole("link", { name: "Food business license" }));

    await waitFor(() => expect(window.location.pathname).toBe("/documents/d-license"));
    await waitFor(() =>
      expect(
        screen.getByRole("heading", { level: 2, name: "Food business license" }),
      ).toHaveFocus(),
    );
  });

  it("puts the counts under the theme and keeps the first and last dates at 375px", async () => {
    renderOverview({}, { width: 375 });
    await loaded();

    const temperature = within(themesTable()).getAllByRole("row")[2];
    expect(
      within(temperature).getByText("3 non-compliant · 1 insufficient evidence · 3 compliant"),
    ).toHaveClass("md:hidden");
    const chart = screen.getByRole("img");
    const first = within(chart).getByText(shortDay(series[0].completedAt));
    const middle = within(chart).getByText(shortDay(series[2].completedAt));
    const last = within(chart).getByText(shortDay(completedAt));
    expect(first).not.toHaveClass("hidden");
    expect(last).not.toHaveClass("hidden");
    expect(middle).toHaveClass("hidden", "@lg:block");
    await expectNoAxeViolations();
  });
});

describe("links into the findings", () => {
  const INSPECTION = "/establishments/e-lisboa/inspections/i-done";
  const inspection = {
    id: "i-done",
    establishmentId: "e-lisboa",
    status: "Completed",
    regulation: "Regulation (EC) 852/2004",
    requestedAt: completedAt,
    startedAt: null,
    completedAt,
    itemsTotal: 2,
    itemsEvaluated: 2,
    themes: [],
    failureReasonCode: null,
    findings: [
      fakeFinding(),
      fakeFinding({
        id: "f-hands",
        theme: "personal_hygiene",
        title: "Food handlers wash their hands",
        verdict: "compliant",
      }),
    ],
  };
  const routes = {
    "/establishments/e-lisboa/inspections": { body: [] },
    [INSPECTION]: { body: inspection },
  };
  const filter = (name: string) => screen.getByRole("combobox", { name });
  const findings = () => screen.findByRole("heading", { level: 2, name: "Findings" });

  it("presets the verdict and focuses the findings", async () => {
    renderOverview(routes);
    await loaded();

    await userEvent.click(
      within(screen.getByRole("list", { name: "Verdicts" })).getByRole("link", {
        name: /^Non-compliant/,
      }),
    );

    await waitFor(() => expect(filter("Filter by verdict")).toHaveValue("non_compliant"));
    expect(filter("Filter by theme")).toHaveValue("");
    await waitFor(async () => expect(await findings()).toHaveFocus());
    await waitFor(() => expect(window.location.search).toBe(""));
    expect(window.location.pathname).toBe("/inspections/i-done");
  });

  it("presets the theme from a risk row", async () => {
    renderOverview(routes);
    await loaded();

    await userEvent.click(
      within(region("Top risk themes")).getByRole("link", { name: "Personal hygiene" }),
    );

    await waitFor(() => expect(filter("Filter by theme")).toHaveValue("personal_hygiene"));
    await waitFor(async () => expect(await findings()).toHaveFocus());
  });

  it("ignores a code that isn't among the options", async () => {
    renderOverview(routes, { path: "/inspections/i-done?verdict=maybe&theme=temperature_control" });

    expect(await findings()).toBeVisible();
    expect(filter("Filter by verdict")).toHaveValue("");
    expect(filter("Filter by theme")).toHaveValue("temperature_control");
    await waitFor(() => expect(window.location.search).toBe(""));
  });

  it("focuses the inspection's title from View inspection", async () => {
    renderOverview(routes);
    await loaded();

    await userEvent.click(screen.getByRole("link", { name: "View inspection" }));

    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: /^Inspection of / })).toHaveFocus(),
    );
  });
});
