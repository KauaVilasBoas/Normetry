import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ActionItem, Finding } from "../api/queries";
import { App } from "../App";
import { createAuth } from "../auth/session";
import { calendarDate, dateTime } from "../copy";
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

// Nothing is live on a completed inspection: the hubs connect and stay quiet.
vi.mock("@microsoft/signalr", () => ({
  HttpTransportType: { WebSockets: 1 },
  HubConnectionState: { Connected: "Connected" },
  LogLevel: { Warning: 3 },
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return {
        state: "Connected",
        start: () => Promise.resolve(),
        stop: () => Promise.resolve(),
        invoke: () => Promise.resolve(),
        on: () => {},
        onreconnecting: () => {},
        onreconnected: () => {},
        onclose: () => {},
      };
    }
  },
}));

const never = () => new Promise<Response>(() => {});
const storageKey = `normetry.establishment.${me.tenantId}.${me.userId}`;
const LIST = "/establishments/e-lisboa/inspections";
const INSPECTION = `${LIST}/i-done`;
const requestedAt = "2026-11-16T16:05:00Z";
const readOnly = ["establishments.read", "inspections.read"];

const action = (id: string, text: string, dueOn: string, status = "Open"): ActionItem => ({
  id,
  text,
  dueOn,
  status,
  updatedBy: null,
  updatedAt: null,
});

const logText = "Registo de outubro: 14/10 câmara fria 9 °C; 15/10 câmara fria 4 °C.";
const logQuote = "14/10 câmara fria 9 °C";

const fridge = fakeFinding({
  id: "f-fridge",
  evidenceCitations: [
    {
      documentId: "d-log",
      documentTitle: "Walk-in temperature log",
      documentType: "Log",
      versionNumber: 3,
      sectionPath: "October",
      language: "pt",
      text: logText,
      quote: logQuote,
      quoteStart: logText.indexOf(logQuote),
      quoteEnd: logText.indexOf(logQuote) + logQuote.length,
    },
  ],
  actionItem: action(
    "a-fridge",
    "Record the walk-in temperature twice a day and act on any reading above 5 °C.",
    dayFromToday(7),
  ),
});
const hands = fakeFinding({
  id: "f-hands",
  theme: "personal_hygiene",
  title: "Food handlers wash their hands at the start of work",
  verdict: "insufficient_evidence",
  confidence: "medium",
  reviewStatus: "Edited",
  reviewedBy: { id: "u-2", displayName: "Marta Manager" },
  reviewedAt: "2026-11-16T18:00:00Z",
  actionItem: action("a-hands", "Provide records of the handwashing checks.", dayFromToday(-2)),
});
hands.original = {
  verdict: "non_compliant",
  severity: "critical",
  rationale: hands.rationale,
  actionText: hands.actionItem!.text,
  dueOn: hands.actionItem!.dueOn,
};
const training = fakeFinding({
  id: "f-training",
  theme: "documentation_training",
  title: "Food handlers are trained in food hygiene",
  severity: "major",
  actionItem: action(
    "a-training",
    "Train every food handler on the handwashing SOP.",
    dayFromToday(30),
    "Done",
  ),
});
const water = fakeFinding({
  id: "f-water",
  theme: "water_premises",
  title: "The water tank is cleaned every six months",
  severity: "major",
  verdict: "compliant",
  reviewStatus: "Accepted",
  reviewedBy: { id: "user-1", displayName: "Olivia Owner" },
  reviewedAt: "2026-11-16T17:00:00Z",
});
const pests = fakeFinding({
  id: "f-pests",
  theme: "pest_control",
  title: "Pest control is done by a licensed company",
  severity: "minor",
  reviewStatus: "Rejected",
  reviewedBy: null,
  reviewedAt: "2026-11-16T17:30:00Z",
  reviewNote: "The contract is in the binder at the front desk.",
  actionItem: action("a-pests", "Provide the pest control contract.", dayFromToday(3)),
});
const all = [water, pests, hands, training, fridge];

// The server's findings: review routes change them, the inspection answers them.
let server: Finding[] = [];
const completed = () => ({
  id: "i-done",
  establishmentId: "e-lisboa",
  status: "Completed",
  regulation: "Regulation (EC) 852/2004",
  requestedAt,
  startedAt: null,
  completedAt: null,
  itemsTotal: 5,
  itemsEvaluated: 5,
  themes: [],
  failureReasonCode: null,
  findings: server,
});

/** A review route: the server applies `change` to the finding and answers it whole. */
const reviewed =
  (id: string, change: (finding: Finding) => Partial<Finding>) => (): Promise<Response> => {
    server = server.map((f) => (f.id === id ? { ...f, ...change(f) } : f));
    return Promise.resolve(Response.json(server.find((f) => f.id === id)));
  };

function renderCompleted(
  routes: Parameters<typeof stubApi>[0] = {},
  { findings = all, permissions = allPermissions, width = 1280 } = {},
) {
  server = findings;
  mockViewport(width);
  localStorage.setItem(storageKey, "e-lisboa");
  window.history.replaceState(null, "", "/inspections/i-done");
  const fetch = stubApi({
    "/me": { body: { ...me, permissions } },
    "/establishments": { body: establishments },
    [LIST]: { body: [] },
    [INSPECTION]: () => Promise.resolve(Response.json(completed())),
    ...routes,
  });
  render(<App auth={createAuth(fakeOidc().oidc)} />);
  return fetch;
}

const calls = (fetch: ReturnType<typeof stubApi>, path: string, method = "GET") =>
  fetch.mock.calls.filter(
    ([input, init]) => input === `/api${path}` && (init?.method ?? "GET") === method,
  );
const bodyOf = (fetch: ReturnType<typeof stubApi>, path: string, method: string) =>
  JSON.parse(calls(fetch, path, method)[0][1]?.body as string) as unknown;

const table = () => screen.findByRole("table", { name: /^Findings of the inspection of / });
const rowTitles = async () =>
  within(await table())
    .getAllByRole("row")
    .slice(1)
    .map((row) => within(row).getAllByRole("button")[0].textContent);
const findingsHeading = () => screen.getByRole("heading", { level: 2, name: "Findings" });
const verdictFilter = () => screen.findByRole("combobox", { name: "Filter by verdict" });
const themeFilter = () => screen.getByRole("combobox", { name: "Filter by theme" });
const optionsOf = (select: HTMLElement) =>
  within(select)
    .getAllByRole("option")
    .map((option) => option.textContent);
const actionPlan = () => screen.findByRole("region", { name: "Action plan" });
const sheetRegion = (sheet: HTMLElement) => sheet.querySelector('[aria-live="polite"]');

/** Opens a finding from its row; resolves to the sheet once its title has focus. */
async function openFinding(finding: Finding) {
  await userEvent.click(await screen.findByRole("button", { name: finding.title }));
  const sheet = screen.getByRole("dialog", { name: finding.title });
  await waitFor(() =>
    expect(within(sheet).getByRole("heading", { level: 2, name: finding.title })).toHaveFocus(),
  );
  return sheet;
}

describe("findings table", () => {
  it("lists the findings most severe first, with their pills and counts", async () => {
    renderCompleted();

    expect(await rowTitles()).toEqual([
      fridge.title,
      hands.title,
      training.title,
      water.title,
      pests.title,
    ]);
    expect(screen.getByRole("button", { name: fridge.title })).toHaveAccessibleDescription(
      "Non-compliant Pending",
    );
    expect(screen.getByRole("button", { name: hands.title })).toHaveAccessibleDescription(
      "Insufficient evidence Edited",
    );
    expect(screen.getByRole("button", { name: fridge.title })).toHaveAttribute(
      "aria-haspopup",
      "dialog",
    );
    expect(screen.getByText("5 findings · 2 pending review")).toBeVisible();
    expect(screen.getByRole("columnheader", { name: "Severity" })).toHaveAttribute(
      "aria-sort",
      "descending",
    );
    expect(optionsOf(await verdictFilter())).toEqual([
      "All verdicts",
      "Non-compliant (3)",
      "Insufficient evidence (1)",
      "Compliant (1)",
    ]);
    expect(optionsOf(themeFilter())).toEqual([
      "All themes",
      "Personal hygiene (1)",
      "Temperature control (1)",
      "Pest control (1)",
      "Water and premises (1)",
      "Documentation and training (1)",
    ]);
    await expectNoAxeViolations();
  });

  it("filters by verdict and theme together, and clears them", async () => {
    renderCompleted();

    await userEvent.selectOptions(await verdictFilter(), "non_compliant");
    expect(await rowTitles()).toEqual([fridge.title, training.title, pests.title]);
    await userEvent.selectOptions(themeFilter(), "pest_control");
    expect(await rowTitles()).toEqual([pests.title]);
    // The counts stay those of every finding.
    expect(screen.getByText("5 findings · 2 pending review")).toBeVisible();

    await userEvent.selectOptions(await verdictFilter(), "compliant");
    expect(
      screen.getByRole("heading", { level: 3, name: "No findings match these filters" }),
    ).toBeVisible();
    expect(screen.queryByRole("table")).toBeNull();
    await expectNoAxeViolations();

    await userEvent.click(screen.getByRole("button", { name: "Clear filters" }));
    expect(await rowTitles()).toHaveLength(5);
    expect(findingsHeading()).toHaveFocus();
    expect(await verdictFilter()).toHaveValue("");
    expect(themeFilter()).toHaveValue("");
  });

  it("sorts minor first and back, keeping verdict then title within a severity", async () => {
    renderCompleted();
    await table();

    await userEvent.click(screen.getByRole("button", { name: "Severity" }));

    expect(screen.getByRole("columnheader", { name: "Severity" })).toHaveAttribute(
      "aria-sort",
      "ascending",
    );
    expect(await rowTitles()).toEqual([
      pests.title,
      training.title,
      water.title,
      fridge.title,
      hands.title,
    ]);
    await userEvent.click(screen.getByRole("button", { name: "Severity" }));
    expect((await rowTitles())[0]).toBe(fridge.title);
  });

  it("says when no finding was kept, and that there are no actions", async () => {
    renderCompleted({}, { findings: [] });

    expect(
      await screen.findByRole("heading", { level: 3, name: "No findings were kept" }),
    ).toBeVisible();
    expect(screen.getByText("0 findings")).toBeVisible();
    expect(screen.queryByRole("combobox", { name: "Filter by verdict" })).toBeNull();
    expect(screen.getByRole("heading", { level: 3, name: "No actions to take" })).toBeVisible();
    await expectNoAxeViolations();
  });

  it("is laid out for 375px without violations", async () => {
    renderCompleted({}, { width: 375 });

    const row = (await screen.findByRole("button", { name: fridge.title })).closest("td")!;
    expect(within(row).getByText("Critical · Temperature control")).toBeInTheDocument();
    // The pills here repeat the hidden cells, which describe the button.
    expect(within(row).getByText("Non-compliant").closest("[aria-hidden]")).toHaveAttribute(
      "aria-hidden",
      "true",
    );
    await expectNoAxeViolations();

    await openFinding(fridge);
    await expectNoAxeViolations();
  });
});

describe("finding sheet", () => {
  it("shows the finding with both quotes in their language and walks the table's order", async () => {
    renderCompleted();
    const sheet = await openFinding(fridge);

    expect(within(sheet).getByText("Finding 1 of 5")).toBeVisible();
    expect(within(sheet).getByRole("button", { name: "Previous finding" })).toHaveAttribute(
      "aria-disabled",
      "true",
    );
    expect(within(sheet).getByText("Anvisa RDC 216/2004 · 4.8.15")).toBeVisible();
    expect(within(sheet).getByText("Version 2004 · Portuguese")).toBeVisible();
    const [regulationQuote, evidenceQuote] = sheet.querySelectorAll("blockquote");
    expect(regulationQuote).toHaveAttribute("lang", "pt");
    expect(regulationQuote.textContent).toBe(
      "Quoted passage:conservados sob refrigeração a temperaturas inferiores a 5ºCEnd of quoted passage.",
    );
    expect(regulationQuote.querySelector("mark")).toHaveTextContent(
      "conservados sob refrigeração a temperaturas inferiores a 5ºC",
    );
    expect(evidenceQuote).toHaveTextContent(logQuote);
    // The whole passage waits behind its toggle, quote highlighted.
    const whole = within(sheet).getAllByText("Show the whole passage")[1].closest("details")!;
    expect(whole).not.toHaveAttribute("open");
    expect(whole.querySelector("mark")).toHaveTextContent(logQuote);
    expect(whole.querySelector('[lang="pt"]')).toHaveTextContent(/^Registo de outubro: .*4 °C\.$/);
    const open = within(sheet).getByRole("link", { name: "Open document" });
    expect(open).toHaveAttribute("href", "/documents/d-log");
    expect(open).toHaveAccessibleDescription("Walk-in temperature log");
    expect(within(sheet).getByText("Version 3 · Portuguese")).toBeVisible();
    expect(within(sheet).getByText(`Due ${calendarDate(dayFromToday(7))}`)).toBeVisible();
    expect(within(sheet).getByText("Due in 7 days")).toBeVisible();
    expect(within(sheet).getByText("No one has reviewed this finding yet.")).toBeVisible();
    await expectNoAxeViolations();

    const next = within(sheet).getByRole("button", { name: "Next finding" });
    await userEvent.click(next);

    expect(screen.getByRole("dialog", { name: hands.title })).toBe(sheet);
    expect(next).toHaveFocus();
    expect(within(sheet).getByText("Finding 2 of 5")).toBeVisible();
    expect(sheetRegion(sheet)).toHaveTextContent(`${hands.title}. Finding 2 of 5.`);
    expect(
      within(sheet).getByText(`Edited by Marta Manager on ${dateTime(hands.reviewedAt!)}.`),
    ).toBeVisible();
    expect(within(sheet).getByText("Overdue")).toBeVisible();
    expect(
      within(sheet).getByText(
        "No passage in this establishment's documents shows this requirement.",
      ),
    ).toBeVisible();
    // Only the verdict changed: only it is listed.
    const original = within(sheet).getByText("Show the original values").closest("details")!;
    expect([...original.querySelectorAll("dt, dd")].map((item) => item.textContent)).toEqual([
      "Verdict",
      "Non-compliant",
    ]);

    for (const n of [3, 4, 5]) {
      await userEvent.click(next);
      expect(within(sheet).getByText(`Finding ${n} of 5`)).toBeVisible();
    }
    expect(next).toHaveAttribute("aria-disabled", "true");
    await userEvent.click(next);
    expect(within(sheet).getByText("Finding 5 of 5")).toBeVisible();
    expect(within(sheet).getByText(/^Rejected by Unknown user on /)).toBeVisible();
    expect(within(sheet).getByText(pests.reviewNote!)).toBeVisible();

    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(screen.getByRole("button", { name: pests.title })).toHaveFocus());
  });

  it("keeps a finding edited out of the filter reachable until it closes", async () => {
    const fetch = renderCompleted({
      [`PATCH ${INSPECTION}/findings/f-fridge`]: reviewed("f-fridge", () => ({
        verdict: "compliant",
        reviewStatus: "Edited",
      })),
    });
    await userEvent.selectOptions(await verdictFilter(), "non_compliant");
    const sheet = await openFinding(fridge);
    expect(within(sheet).getByText("Finding 1 of 3")).toBeVisible();

    await userEvent.click(within(sheet).getByRole("button", { name: "Edit" }));
    const dialog = await screen.findByRole("dialog", { name: "Edit finding" });
    const verdict = within(dialog).getByRole("combobox", { name: "Verdict" });
    await waitFor(() => expect(verdict).toHaveFocus());
    const save = within(dialog).getByRole("button", { name: "Save changes" });
    expect(save).toHaveAttribute("aria-disabled", "true");
    await expectNoAxeViolations();
    await userEvent.selectOptions(verdict, "compliant");
    expect(save).not.toHaveAttribute("aria-disabled", "true");
    await userEvent.click(save);

    await waitFor(() => expect(dialog).not.toBeInTheDocument());
    expect(bodyOf(fetch, `${INSPECTION}/findings/f-fridge`, "PATCH")).toEqual({
      verdict: "compliant",
    });
    await waitFor(() => expect(within(sheet).getByRole("button", { name: "Edit" })).toHaveFocus());
    expect(sheetRegion(sheet)).toHaveTextContent("Finding updated.");
    expect(within(sheet).getByText("Compliant")).toBeVisible();

    await userEvent.click(within(sheet).getByRole("button", { name: "Next finding" }));
    expect(within(sheet).getByText("Finding 2 of 3")).toBeVisible();
    await userEvent.click(within(sheet).getByRole("button", { name: "Previous finding" }));
    expect(screen.getByRole("dialog", { name: fridge.title })).toBe(sheet);

    await userEvent.click(within(sheet).getByRole("button", { name: "Close finding" }));
    // Its row is filtered out now: focus goes to the card's heading.
    await waitFor(() => expect(findingsHeading()).toHaveFocus());
    expect(await rowTitles()).toEqual([training.title, pests.title]);
  });

  it("opens a cited document on its own page", async () => {
    renderCompleted({
      "/establishments/e-lisboa/documents": { body: [] },
      "/documents/d-log": {
        body: {
          id: "d-log",
          establishmentId: "e-lisboa",
          title: "Walk-in temperature log",
          type: "Log",
          createdAt: requestedAt,
          updatedAt: requestedAt,
          currentVersionNumber: 3,
          versions: [],
        },
      },
    });
    const sheet = await openFinding(fridge);

    await userEvent.click(within(sheet).getByRole("link", { name: "Open document" }));

    await waitFor(() => expect(window.location.pathname).toBe("/documents/d-log"));
    await waitFor(() =>
      expect(
        screen.getByRole("heading", { level: 2, name: "Walk-in temperature log" }),
      ).toHaveFocus(),
    );
  });
});

describe("review", () => {
  it("accepts a finding, announces it and holds Accept", async () => {
    const accepted = { id: "user-1", displayName: "Olivia Owner" };
    const fetch = renderCompleted({
      [`POST ${INSPECTION}/findings/f-fridge/accept`]: reviewed("f-fridge", () => ({
        reviewStatus: "Accepted",
        reviewedBy: accepted,
        reviewedAt: "2026-11-17T09:00:00Z",
      })),
    });
    const sheet = await openFinding(fridge);
    const before = calls(fetch, INSPECTION).length;
    const accept = within(sheet).getByRole("button", { name: "Accept" });

    await userEvent.click(accept);

    await waitFor(() => expect(accept).toHaveAttribute("aria-disabled", "true"));
    expect(accept).toHaveFocus();
    expect(accept).toHaveAccessibleDescription(
      `Accepted by Olivia Owner on ${dateTime("2026-11-17T09:00:00Z")}.`,
    );
    expect(sheetRegion(sheet)).toHaveTextContent("Finding accepted.");
    expect(within(sheet).getByText("Accepted")).toBeVisible();
    await waitFor(() => expect(calls(fetch, INSPECTION).length).toBeGreaterThan(before));
    await expectNoAxeViolations();

    await userEvent.click(accept);
    expect(calls(fetch, `${INSPECTION}/findings/f-fridge/accept`, "POST")).toHaveLength(1);
  });

  it("rejects a finding with a required reason", async () => {
    const note = "The log is from the walk-in that was replaced.";
    const path = `${INSPECTION}/findings/f-fridge/reject`;
    const fetch = renderCompleted({
      [`POST ${path}`]: reviewed("f-fridge", () => ({
        reviewStatus: "Rejected",
        reviewNote: note,
        reviewedBy: { id: "user-1", displayName: "Olivia Owner" },
        reviewedAt: "2026-11-17T09:00:00Z",
      })),
    });
    const sheet = await openFinding(fridge);

    await userEvent.click(within(sheet).getByRole("button", { name: "Reject" }));
    const dialog = await screen.findByRole("dialog", { name: "Reject finding" });
    const reason = within(dialog).getByRole("textbox", { name: "Reason" });
    await waitFor(() => expect(reason).toHaveFocus());
    const submit = within(dialog).getByRole("button", { name: "Reject finding" });
    await userEvent.click(submit);

    expect(reason).toHaveFocus();
    expect(reason).toHaveAccessibleDescription(
      "Say why this finding is wrong. Up to 1,000 characters. Enter a reason.",
    );
    expect(reason).toHaveAttribute("maxlength", "1000");
    expect(calls(fetch, path, "POST")).toHaveLength(0);
    await expectNoAxeViolations();

    await userEvent.paste(`  ${note}  `);
    await userEvent.click(submit);

    await waitFor(() => expect(dialog).not.toBeInTheDocument());
    expect(bodyOf(fetch, path, "POST")).toEqual({ note });
    await waitFor(() =>
      expect(within(sheet).getByRole("button", { name: "Reject" })).toHaveFocus(),
    );
    expect(sheetRegion(sheet)).toHaveTextContent("Finding rejected.");
    expect(within(sheet).getByText(note)).toBeVisible();

    // Reopened, it holds the current reason.
    await userEvent.click(within(sheet).getByRole("button", { name: "Reject" }));
    expect(
      within(await screen.findByRole("dialog", { name: "Reject finding" })).getByRole("textbox", {
        name: "Reason",
      }),
    ).toHaveValue(note);
  });

  it("edits only what changed, an action with its due date", async () => {
    const path = `${INSPECTION}/findings/f-water`;
    const due = dayFromToday(10);
    const fetch = renderCompleted({
      [`PATCH ${path}`]: reviewed("f-water", () => ({
        reviewStatus: "Edited",
        rationale: "The tank was last cleaned in March.",
        actionItem: action("a-water", "Clean the tank and keep the receipt.", due),
      })),
    });
    const sheet = await openFinding(water);
    expect(within(sheet).getByText("No action needed.")).toBeVisible();

    await userEvent.click(within(sheet).getByRole("button", { name: "Edit" }));
    const dialog = await screen.findByRole("dialog", { name: "Edit finding" });
    const save = within(dialog).getByRole("button", { name: "Save changes" });
    const rationale = within(dialog).getByRole("textbox", { name: "Rationale" });
    const dueDate = within(dialog).getByLabelText("Due date");
    expect(within(dialog).getByRole("textbox", { name: "Action" })).toHaveValue("");
    await userEvent.click(within(dialog).getByRole("textbox", { name: "Action" }));
    await userEvent.paste("Clean the tank and keep the receipt.");
    await userEvent.click(save);

    // An action needs its due date.
    await waitFor(() => expect(dueDate).toHaveFocus());
    expect(dueDate).toHaveAccessibleDescription("Choose a due date.");
    await expectNoAxeViolations();

    fireEvent.change(dueDate, { target: { value: due } });
    await userEvent.clear(rationale);
    await userEvent.click(save);
    await waitFor(() => expect(rationale).toHaveFocus());
    expect(rationale).toHaveAccessibleDescription("Enter a rationale.");
    expect(calls(fetch, path, "PATCH")).toHaveLength(0);

    await userEvent.paste("The tank was last cleaned in March.");
    await userEvent.click(save);

    await waitFor(() => expect(dialog).not.toBeInTheDocument());
    expect(bodyOf(fetch, path, "PATCH")).toEqual({
      rationale: "The tank was last cleaned in March.",
      actionText: "Clean the tank and keep the receipt.",
      dueOn: due,
    });
    await waitFor(() => expect(within(sheet).getByRole("button", { name: "Edit" })).toHaveFocus());
    expect(sheetRegion(sheet)).toHaveTextContent("Finding updated.");
    expect(within(sheet).getByText("Clean the tank and keep the receipt.")).toBeVisible();
  });

  it("keeps a finding's action: it can't be cleared", async () => {
    const fetch = renderCompleted();
    const sheet = await openFinding(fridge);
    await userEvent.click(within(sheet).getByRole("button", { name: "Edit" }));
    const dialog = await screen.findByRole("dialog", { name: "Edit finding" });
    const actionField = within(dialog).getByRole("textbox", { name: "Action" });

    await userEvent.clear(actionField);
    await userEvent.click(within(dialog).getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(actionField).toHaveFocus());
    expect(actionField).toHaveAccessibleDescription(
      "What the establishment should do, in one or two sentences. Describe the action.",
    );
    expect(calls(fetch, `${INSPECTION}/findings/f-fridge`, "PATCH")).toHaveLength(0);
  });

  it("says a refused review in the dialog, which keeps its draft", async () => {
    renderCompleted({
      [`POST ${INSPECTION}/findings/f-fridge/reject`]: {
        status: 404,
        body: { code: "finding.not_found" },
      },
      [`PATCH ${INSPECTION}/findings/f-fridge`]: { status: 500 },
    });
    const sheet = await openFinding(fridge);

    await userEvent.click(within(sheet).getByRole("button", { name: "Reject" }));
    let dialog = await screen.findByRole("dialog", { name: "Reject finding" });
    await userEvent.type(within(dialog).getByRole("textbox", { name: "Reason" }), "Wrong log.");
    await userEvent.click(within(dialog).getByRole("button", { name: "Reject finding" }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "This finding isn't available anymore.",
    );
    expect(within(dialog).getByRole("textbox", { name: "Reason" })).toHaveValue("Wrong log.");
    await userEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

    await userEvent.click(within(sheet).getByRole("button", { name: "Edit" }));
    dialog = await screen.findByRole("dialog", { name: "Edit finding" });
    await userEvent.selectOptions(
      within(dialog).getByRole("combobox", { name: "Severity" }),
      "major",
    );
    await userEvent.click(within(dialog).getByRole("button", { name: "Save changes" }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "Couldn't save the review. Try again.",
    );
    expect(within(dialog).getByRole("combobox", { name: "Severity" })).toHaveValue("major");
  });

  it("says when the permission was taken away, and refetches /me once the sheet closes", async () => {
    const fetch = renderCompleted({
      "/me": [
        { body: me },
        { body: { ...me, permissions: allPermissions.filter((p) => p !== "inspections.review") } },
      ],
      [`POST ${INSPECTION}/findings/f-fridge/accept`]: {
        status: 403,
        body: { code: "permission.denied" },
      },
    });
    const sheet = await openFinding(fridge);
    const accept = within(sheet).getByRole("button", { name: "Accept" });

    await userEvent.click(accept);

    expect(await within(sheet).findByRole("alert")).toHaveTextContent(
      "You no longer have permission to do this.",
    );
    expect(accept).toHaveFocus();
    expect(calls(fetch, "/me")).toHaveLength(1);

    await userEvent.click(within(sheet).getByRole("button", { name: "Close finding" }));

    await waitFor(() => expect(calls(fetch, "/me")).toHaveLength(2));
    await waitFor(() =>
      expect(
        within(screen.getByRole("region", { name: "Action plan" })).queryAllByRole("checkbox"),
      ).toHaveLength(0),
    );
    const reopened = await openFinding(fridge);
    expect(within(reopened).queryByRole("button", { name: "Accept" })).toBeNull();
  });
});

describe("action plan", () => {
  const names = async () =>
    within(await actionPlan())
      .getAllByRole("listitem")
      .map((item) => item.textContent);

  it("lists open actions by due date, without rejected findings', with their due dates", async () => {
    renderCompleted();

    expect(await names()).toEqual([
      `${hands.actionItem!.text}${hands.title} · Due ${calendarDate(dayFromToday(-2))}Overdue`,
      `${fridge.actionItem!.text}${fridge.title} · Due ${calendarDate(dayFromToday(7))}Due in 7 days`,
      `${training.actionItem!.text}${training.title} · Due ${calendarDate(dayFromToday(30))}`,
    ]);
    const plan = await actionPlan();
    expect(within(plan).getByText("2 open · 1 done")).toBeVisible();
    const boxes = within(plan).getAllByRole("checkbox");
    expect(boxes[1]).toHaveAccessibleName(fridge.actionItem!.text);
    expect(boxes[1]).toHaveAccessibleDescription(
      `${fridge.title} · Due ${calendarDate(dayFromToday(7))}`,
    );
    expect(boxes.map((box) => (box as HTMLInputElement).checked)).toEqual([false, false, true]);
    await expectNoAxeViolations();
  });

  it("marks an action done at once, and keeps the order", async () => {
    const path = `${INSPECTION}/action-items/a-fridge/done`;
    const fetch = renderCompleted({
      [`POST ${path}`]: reviewed("f-fridge", (f) => ({
        actionItem: { ...f.actionItem!, status: "Done" },
      })),
    });
    const plan = await actionPlan();
    const before = await names();

    await userEvent.click(within(plan).getAllByRole("checkbox")[1]);

    expect(within(plan).getAllByRole("checkbox")[1]).toBeChecked();
    expect(await within(plan).findByText("1 open · 2 done")).toBeVisible();
    await waitFor(() => expect(calls(fetch, path, "POST")).toHaveLength(1));
    expect((await names()).map((name) => name?.slice(0, 20))).toEqual(
      before.map((name) => name?.slice(0, 20)),
    );
    expect(within(plan).queryByText("Due in 7 days")).toBeNull();
  });

  it("reverts a toggle that fails, and ignores clicks while one runs", async () => {
    const reopen = `${INSPECTION}/action-items/a-training/reopen`;
    const fetch = renderCompleted({
      [`POST ${INSPECTION}/action-items/a-hands/done`]: { status: 500 },
      [`POST ${reopen}`]: never,
    });
    const plan = await actionPlan();
    const boxes = () => within(plan).getAllByRole("checkbox");

    await userEvent.click(boxes()[0]);
    expect(await within(plan).findByRole("alert")).toHaveTextContent(
      "Couldn't update the action. Try again.",
    );
    expect(boxes()[0]).not.toBeChecked();
    expect(within(plan).getByText("2 open · 1 done")).toBeVisible();
    await expectNoAxeViolations();

    await userEvent.click(boxes()[2]);
    expect(boxes()[2]).not.toBeChecked();
    await userEvent.click(boxes()[2]);
    expect(boxes()[2]).not.toBeChecked();
    expect(calls(fetch, reopen, "POST")).toHaveLength(1);
  });

  it("shows actions and findings read-only without inspections.review", async () => {
    renderCompleted({}, { permissions: readOnly });

    expect(await names()).toEqual([
      expect.stringMatching(/^Open: Provide records of the handwashing checks\./),
      expect.stringMatching(/^Open: Record the walk-in temperature/),
      expect.stringMatching(/^Done: Train every food handler/),
    ]);
    expect(within(await actionPlan()).queryByRole("checkbox")).toBeNull();
    await expectNoAxeViolations();

    const sheet = await openFinding(fridge);
    expect(within(sheet).queryByRole("button", { name: "Accept" })).toBeNull();
    expect(within(sheet).queryByRole("button", { name: "Reject" })).toBeNull();
    expect(within(sheet).queryByRole("button", { name: "Edit" })).toBeNull();
    // Without documents.read the document is named, not linked.
    expect(within(sheet).getByText("Walk-in temperature log")).toBeVisible();
    expect(within(sheet).queryByRole("link", { name: "Open document" })).toBeNull();
    await expectNoAxeViolations();
  });
});
