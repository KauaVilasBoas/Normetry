import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { App } from "../App";
import { createAuth } from "../auth/session";
import { calendarDate } from "../copy";
import {
  allPermissions,
  dayFromToday,
  establishments,
  expectNoAxeViolations,
  fakeOidc,
  me,
  mockViewport,
  stubApi,
} from "../test/fakes";
import { hubs, latestHub } from "../test/signalr";
import type { DocumentStatusChange } from "./documents";

// A hub per connection the screen opens; the test plays the server's side.
vi.mock("@microsoft/signalr", () => import("../test/signalr"));

const push = (change: Partial<DocumentStatusChange>) =>
  act(() =>
    latestHub().events.get("DocumentStatusChanged")?.({
      establishmentId: "e-lisboa",
      versionId: "v-x",
      failureCode: null,
      failureReason: null,
      changedAt: "2026-11-16T10:00:00Z",
      ...change,
    }),
  );

const never = () => new Promise<Response>(() => {});
const storageKey = `normetry.establishment.${me.tenantId}.${me.userId}`;

const version = (number: number, status: string, extra: object = {}) => ({
  id: `v-${number}`,
  number,
  fileName: `cold-storage-v${number}.pdf`,
  contentType: "application/pdf",
  size: 2_516_582,
  status,
  failureCode: null,
  failureReason: null,
  uploadedBy: { id: "u-2", displayName: "Ana Vieira" },
  uploadedAt: "2026-11-14T10:00:00Z",
  statusChangedAt: "2026-11-14T10:00:00Z",
  chunkCount: null,
  pageCount: null,
  language: null,
  ...extra,
});

const fields = (
  id: string,
  title: string,
  type: string,
  current: number | null,
  expiresOn: string | null = null,
) => ({
  id,
  establishmentId: "e-lisboa",
  title,
  type,
  createdAt: "2026-11-01T10:00:00Z",
  updatedAt: "2026-11-14T10:00:00Z",
  currentVersionNumber: current,
  expiresOn,
});

const list = [
  { ...fields("d-cold", "Cold storage SOP", "Sop", 3), latestVersion: version(3, "Indexed") },
  {
    ...fields("d-temps", "Walk-in temperature log", "Log", 4),
    latestVersion: version(5, "Processing"),
  },
  {
    ...fields("d-license", "Food business license", "License", null),
    latestVersion: version(1, "Failed", { failureCode: "no_text_layer" }),
  },
  {
    ...fields("d-pest", "Pest control report", "Report", null),
    latestVersion: version(1, "Uploaded"),
  },
];

const processing = {
  ...fields("d-cold", "Cold storage SOP", "Sop", 3),
  versions: [
    version(4, "Processing"),
    version(3, "Indexed"),
    version(2, "Failed", {
      failureCode: "no_text_layer",
      uploadedBy: { id: "u-3", displayName: null },
    }),
  ],
};
const indexed = { ...processing, versions: processing.versions.slice(1) };

function renderDocuments(
  routes: Parameters<typeof stubApi>[0] = {},
  { path = "/documents", width = 1280, permissions = allPermissions, remembered = true } = {},
) {
  mockViewport(width);
  if (remembered) localStorage.setItem(storageKey, "e-lisboa");
  window.history.replaceState(null, "", path);
  const fetch = stubApi({
    "/me": { body: { ...me, permissions } },
    "/establishments": { body: establishments },
    "/establishments/e-lisboa/documents": { body: list },
    "/documents/d-cold": { body: processing },
    ...routes,
  });
  render(<App auth={createAuth(fakeOidc().oidc)} />);
  return fetch;
}

const card = () => screen.getByRole("region", { name: "Bistro Lisboa" });
const rows = () => within(card()).getAllByRole("row").slice(1);
const calls = (fetch: ReturnType<typeof stubApi>, path: string, method = "GET") =>
  fetch.mock.calls.filter(
    ([input, init]) => input === `/api${path}` && (init?.method ?? "GET") === method,
  );
const lastForm = (fetch: ReturnType<typeof stubApi>) =>
  fetch.mock.calls.findLast(([, init]) => init?.method === "POST")?.[1]?.body as FormData;

const pdf = (name = "cold-storage-sop.pdf") =>
  new File(["%PDF-1.7"], name, { type: "application/pdf" });
const fileInput = () => document.querySelector<HTMLInputElement>('input[type="file"]')!;

describe("documents list", () => {
  it("lists the documents with their status, version and type, newest updated first", async () => {
    renderDocuments();

    const link = await screen.findByRole("link", { name: "Walk-in temperature log" });
    expect(link).toHaveAccessibleDescription("Processing Version 5");
    expect(link).toHaveAttribute("href", "/documents/d-temps");
    expect(within(card()).getByText("4 documents")).toBeVisible();
    expect(rows().map((row) => row.textContent)).toEqual([
      expect.stringMatching(/^Cold storage SOP.*SOPIndexedv3Version 3/),
      expect.stringMatching(/^Walk-in temperature log.*LogProcessing Version 5v4Version 4/),
      expect.stringMatching(/^Food business license.*LicenseFailed No text layer—No indexed/),
      expect.stringMatching(/^Pest control report.*ReportWaiting—No indexed/),
    ]);
    expect(
      within(card()).getByRole("table", { name: "Documents for Bistro Lisboa" }),
    ).toBeVisible();
    expect(screen.getByRole("heading", { level: 1, name: "Documents" })).toBeVisible();
    expect(document.title).toBe("Documents · Normetry");
    expect(
      within(screen.getByRole("navigation", { name: "Main" })).getByRole("link", {
        name: "Documents",
      }),
    ).toHaveAttribute("aria-current", "page");
    expect(hubs.urls).toMatchObject([
      { url: "/hubs/documents", options: { skipNegotiation: true } },
    ]);
    await waitFor(() =>
      expect(latestHub().invoke).toHaveBeenCalledWith("JoinEstablishment", "e-lisboa"),
    );
    await expectNoAxeViolations();
  });

  it("shows skeleton rows while loading", async () => {
    renderDocuments({ "/establishments/e-lisboa/documents": never });

    await waitFor(() => expect(card()).toHaveAttribute("aria-busy", "true"));
    expect(within(card()).getByText("Loading documents…")).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("shows the empty state, with Upload only for who may upload", async () => {
    renderDocuments(
      { "/establishments/e-lisboa/documents": { body: [] } },
      { permissions: ["establishments.read", "documents.read"] },
    );

    expect(
      await screen.findByText("Documents uploaded for Bistro Lisboa appear here."),
    ).toBeVisible();
    expect(within(card()).getByText("0 documents")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Upload document" })).toBeNull();
    expect(screen.queryByRole("combobox", { name: "Filter by expiry" })).toBeNull();
    await expectNoAxeViolations();
  });

  it("shows a load error and recovers with Try again", async () => {
    renderDocuments({
      "/establishments/e-lisboa/documents": [{ status: 500 }, { status: 500 }, { body: list }],
    });

    // One retry, a second later, before the error shows.
    const retry = await screen.findByRole("button", { name: "Try again" }, { timeout: 3000 });
    expect(within(card()).getByRole("alert")).toHaveTextContent("Couldn't load documents");
    await expectNoAxeViolations();
    await userEvent.click(retry);

    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus(),
    );
    expect(rows()).toHaveLength(4);
  });

  it("keeps the status column and the title's meta line below 768px", async () => {
    renderDocuments({}, { width: 375 });

    await screen.findByRole("link", { name: "Cold storage SOP" });
    await expectNoAxeViolations();
  });
});

describe("upload", () => {
  it("checks the form, uploads, and shows the new row as Waiting", async () => {
    const uploaded = {
      ...fields("d-new", "Allergen matrix", "Sop", null),
      versions: [version(1, "Uploaded", { fileName: "allergens.pdf" })],
    };
    // The server's list: the new document joins it once it is uploaded.
    let server: unknown[] = list;
    const fetch = renderDocuments({
      "/establishments/e-lisboa/documents": () => Promise.resolve(Response.json(server)),
      "POST /establishments/e-lisboa/documents": () => {
        server = [{ ...uploaded, latestVersion: uploaded.versions[0] }, ...list];
        return Promise.resolve(Response.json(uploaded, { status: 202 }));
      },
    });
    const user = userEvent.setup();
    const trigger = await screen.findByRole("button", { name: "Upload document" });

    await user.click(trigger);
    const dialog = await screen.findByRole("dialog", { name: "Upload document" });
    expect(dialog).toHaveAccessibleDescription("Add a document to Bistro Lisboa.");
    const zone = within(dialog).getByRole("button", { name: "Choose a file or drop it here" });
    await waitFor(() => expect(zone).toHaveFocus());
    expect(zone).toHaveAccessibleDescription("PDF, DOCX, TXT or CSV · up to 10 MB and 100 pages");
    await expectNoAxeViolations();

    await user.click(within(dialog).getByRole("button", { name: "Upload" }));
    await waitFor(() => expect(zone).toHaveFocus());
    expect(zone).toHaveAccessibleDescription(/Choose a file to upload\.$/);
    expect(within(dialog).getByRole("textbox", { name: "Title" })).toHaveAccessibleDescription(
      "Enter a title.",
    );
    expect(within(dialog).getByRole("combobox", { name: "Type" })).toHaveAccessibleDescription(
      "Choose a type.",
    );
    await expectNoAxeViolations();

    await user.upload(fileInput(), pdf("allergens.pdf"));
    const chosen = within(dialog).getByRole("button", {
      name: "allergens.pdf 0 kB Choose another file",
    });
    expect(chosen).toHaveFocus();
    expect(within(dialog).getByText("allergens.pdf chosen.")).toHaveAttribute(
      "aria-live",
      "polite",
    );
    const title = within(dialog).getByRole("textbox", { name: "Title" });
    expect(title).toHaveValue("allergens");
    await user.clear(title);
    await user.type(title, "Allergen matrix");
    await user.selectOptions(within(dialog).getByRole("combobox", { name: "Type" }), "Sop");
    await user.click(within(dialog).getByRole("button", { name: "Upload" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(trigger).toHaveFocus();
    const form = lastForm(fetch);
    expect(form.get("title")).toBe("Allergen matrix");
    expect(form.get("type")).toBe("Sop");
    expect(form.get("expiresOn")).toBeNull();
    expect((form.get("file") as File).name).toBe("allergens.pdf");
    expect(
      screen.getByText("“Allergen matrix” uploaded. It's waiting to be processed."),
    ).toBeInTheDocument();
    expect(
      await screen.findByRole("link", { name: "Allergen matrix" }),
    ).toHaveAccessibleDescription("Waiting");
  });

  it("refuses a file of the wrong type or size before sending it", async () => {
    renderDocuments();
    const user = userEvent.setup({ applyAccept: false });
    await user.click(await screen.findByRole("button", { name: "Upload document" }));
    const dialog = await screen.findByRole("dialog");

    await user.upload(fileInput(), new File(["png"], "fridge.png", { type: "image/png" }));
    const zone = within(dialog).getByRole("button", { name: "Choose a file or drop it here" });
    expect(zone).toHaveAccessibleDescription(/“fridge\.png” isn't a PDF, DOCX, TXT or CSV file\.$/);

    const big = pdf("big.pdf");
    Object.defineProperty(big, "size", { value: 10 * 1024 * 1024 + 1 });
    await user.upload(fileInput(), big);
    expect(zone).toHaveAccessibleDescription(/“big\.pdf” is larger than 10 MB\.$/);
  });

  it("opens with a file dropped on the card, and refuses two", async () => {
    renderDocuments();
    await screen.findByRole("link", { name: "Cold storage SOP" });

    fireEvent.drop(card(), { dataTransfer: { files: [pdf(), pdf("b.pdf")], types: ["Files"] } });
    let dialog = await screen.findByRole("dialog");
    expect(
      within(dialog).getByRole("button", { name: "Choose a file or drop it here" }),
    ).toHaveAccessibleDescription(/Drop one file at a time\.$/);
    await userEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(screen.getByRole("button", { name: "Upload document" })).toHaveFocus();

    fireEvent.drop(card(), { dataTransfer: { files: [pdf()], types: ["Files"] } });
    dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("textbox", { name: "Title" })).toHaveValue("cold-storage-sop");
  });

  it("shows the server's refusals in the dialog", async () => {
    renderDocuments({
      "POST /establishments/e-lisboa/documents": [
        { status: 409, body: { code: "document.limit_reached" } },
        { status: 400, body: { code: "document.invalid_content" } },
      ],
    });
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Upload document" }));
    const dialog = await screen.findByRole("dialog");
    await user.upload(fileInput(), pdf());
    await user.selectOptions(within(dialog).getByRole("combobox", { name: "Type" }), "License");

    await user.click(within(dialog).getByRole("button", { name: "Upload" }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "This establishment has reached its document limit. Delete a document before uploading another.",
    );

    await user.click(within(dialog).getByRole("button", { name: "Upload" }));
    const zone = within(dialog).getByRole("button", { name: "Choose a file or drop it here" });
    await waitFor(() => expect(zone).toHaveFocus());
    expect(zone).toHaveAccessibleDescription(
      /“cold-storage-sop\.pdf” isn't a valid PDF, DOCX, TXT or CSV file\.$/,
    );
  });
});

describe("live status", () => {
  it("refetches and announces a version that finished", async () => {
    const fetch = renderDocuments();
    await screen.findByRole("link", { name: "Walk-in temperature log" });
    await waitFor(() => expect(latestHub().invoke).toHaveBeenCalled());
    const before = calls(fetch, "/establishments/e-lisboa/documents").length;

    push({
      documentId: "d-temps",
      versionNumber: 5,
      status: "Processing",
      currentVersionNumber: 4,
    });
    push({ documentId: "d-temps", versionNumber: 5, status: "Indexed", currentVersionNumber: 5 });
    push({
      documentId: "d-license",
      versionNumber: 1,
      status: "Failed",
      failureCode: "parse_error",
      currentVersionNumber: null,
    });
    push({ establishmentId: "e-sao-paulo", documentId: "d-other", status: "Indexed" });

    await waitFor(() =>
      expect(calls(fetch, "/establishments/e-lisboa/documents").length).toBeGreaterThan(before),
    );
    const region = screen.getByText("“Walk-in temperature log” is indexed.").parentElement;
    expect(region).toHaveAttribute("aria-live", "polite");
    expect(region?.textContent).toBe(
      "“Walk-in temperature log” is indexed.“Food business license” couldn't be indexed: Couldn't read the file.",
    );
  });

  it("says when updates paused, and Reconnect starts over and refetches", async () => {
    const fetch = renderDocuments();
    await screen.findByRole("link", { name: "Cold storage SOP" });
    await waitFor(() => expect(latestHub().invoke).toHaveBeenCalled());

    act(() => latestHub().reconnecting());
    // The status region is always there (so what arrives in it is heard), empty while live.
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
    expect(
      await screen.findByText("Live updates paused. Reconnecting…", {}, { timeout: 3000 }),
    ).toBeVisible();
    act(() => latestHub().closed());
    const status = screen.getByRole("status");
    expect(status).toHaveTextContent("Live updates stopped. Statuses may be out of date.");
    await expectNoAxeViolations();
    const before = calls(fetch, "/establishments/e-lisboa/documents").length;

    await userEvent.click(within(status).getByRole("button", { name: "Reconnect" }));

    await waitFor(() => expect(status).toBeEmptyDOMElement());
    expect(hubs.all).toHaveLength(2);
    expect(latestHub().invoke).toHaveBeenCalledWith("JoinEstablishment", "e-lisboa");
    expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus();
    await waitFor(() =>
      expect(calls(fetch, "/establishments/e-lisboa/documents").length).toBeGreaterThan(before),
    );
  });
});

describe("document detail", () => {
  it("opens from the list with its versions and the processing notice", async () => {
    renderDocuments();
    await userEvent.click(await screen.findByRole("link", { name: "Cold storage SOP" }));

    const title = await screen.findByRole("heading", { level: 2, name: "Cold storage SOP" });
    await waitFor(() => expect(title).toHaveFocus());
    expect(await screen.findByText("SOP · Bistro Lisboa")).toBeVisible();
    await waitFor(() => expect(document.title).toBe("Cold storage SOP · Documents · Normetry"));
    const versions = screen.getByRole("region", { name: "Versions" });
    expect(within(versions).getByText("Version 4 is processing.")).toBeVisible();
    expect(
      within(versions).getByText("Version 3 stays in use until version 4 is indexed."),
    ).toBeVisible();
    const upload = screen.getByRole("button", { name: "Upload new version" });
    expect(upload).toHaveAttribute("aria-disabled", "true");
    expect(upload).toHaveAccessibleDescription(
      "You can upload another version when this one finishes.",
    );
    const [v4, v3, v2] = within(versions).getAllByRole("row").slice(1);
    expect(v4).toHaveTextContent(/Version 4.*Processing/);
    expect(within(v3).getByText("Current")).toBeVisible();
    expect(v2).toHaveTextContent(/Failed No text layer.*Unknown user/);
    await expectNoAxeViolations();

    await userEvent.click(upload);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("uploads a new version, after which Upload new version waits for it", async () => {
    const fetch = renderDocuments(
      {
        "/documents/d-cold": { body: indexed },
        "POST /documents/d-cold/versions": { status: 202, body: processing },
      },
      { path: "/documents/d-cold" },
    );
    const user = userEvent.setup();
    const upload = await screen.findByRole("button", { name: "Upload new version" });
    expect(upload).not.toHaveAttribute("aria-disabled", "true");

    await user.click(upload);
    const dialog = await screen.findByRole("dialog", { name: "Upload a new version" });
    expect(dialog).toHaveAccessibleDescription(
      "“Cold storage SOP” keeps its title and type. Version 3 stays in use until the new one is indexed.",
    );
    expect(within(dialog).queryByRole("textbox")).toBeNull();
    await expectNoAxeViolations();
    await user.upload(fileInput(), pdf("cold-storage-v4.pdf"));
    await user.click(within(dialog).getByRole("button", { name: "Upload" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(upload).toHaveFocus();
    expect(upload).toHaveAttribute("aria-disabled", "true");
    expect((lastForm(fetch).get("file") as File).name).toBe("cold-storage-v4.pdf");
    expect(lastForm(fetch).get("title")).toBeNull();
    expect(
      screen.getByText("New version uploaded. It's waiting to be processed."),
    ).toBeInTheDocument();
  });

  it("announces the open document's version by number", async () => {
    renderDocuments({}, { path: "/documents/d-cold" });
    await screen.findByText("Version 4 is processing.");
    await waitFor(() => expect(latestHub().invoke).toHaveBeenCalled());

    push({
      documentId: "d-cold",
      versionNumber: 4,
      status: "Failed",
      failureCode: "too_many_pages",
      currentVersionNumber: 3,
    });

    expect(screen.getByText("Version 4 couldn't be indexed: Too many pages.")).toBeInTheDocument();
  });

  it("deletes after confirmation, then focuses the list", async () => {
    const fetch = renderDocuments(
      { "DELETE /documents/d-cold": { status: 204 } },
      { path: "/documents/d-cold" },
    );
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Delete document" }));
    const dialog = await screen.findByRole("alertdialog", { name: "Delete “Cold storage SOP”?" });
    expect(dialog).toHaveAccessibleDescription(
      "The document and all its versions are removed from Bistro Lisboa. This can't be undone.",
    );
    expect(within(dialog).getByRole("button", { name: "Cancel" })).toHaveFocus();
    await expectNoAxeViolations();
    await user.click(within(dialog).getByRole("button", { name: "Delete" }));

    await waitFor(() => expect(window.location.pathname).toBe("/documents"));
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus(),
    );
    expect(screen.getByText("“Cold storage SOP” deleted.")).toBeInTheDocument();
    expect(calls(fetch, "/documents/d-cold", "DELETE")).toHaveLength(1);
  });

  it("shows skeletons while it loads, with the list's title when known", async () => {
    renderDocuments({ "/documents/d-cold": never }, { path: "/documents/d-cold" });

    expect(await screen.findByText("Loading document…")).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("shows a load error and recovers with Try again", async () => {
    renderDocuments(
      { "/documents/d-cold": [{ status: 500 }, { status: 500 }, { body: processing }] },
      { path: "/documents/d-cold" },
    );

    const retry = await screen.findByRole("button", { name: "Try again" }, { timeout: 3000 });
    expect(screen.getByRole("alert")).toHaveTextContent("Couldn't load this document");
    await expectNoAxeViolations();
    await userEvent.click(retry);

    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Cold storage SOP" })).toHaveFocus(),
    );
  });

  it("moves focus to the title when a refetch after a reconnect clears the load error", async () => {
    renderDocuments(
      { "/documents/d-cold": [{ status: 500 }, { status: 500 }, { body: processing }] },
      { path: "/documents/d-cold" },
    );
    const retry = await screen.findByRole("button", { name: "Try again" }, { timeout: 3000 });
    retry.focus();

    act(() => latestHub().reconnected());

    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Cold storage SOP" })).toHaveFocus(),
    );
  });

  it("shows a document that is gone as not found, with the way back", async () => {
    renderDocuments(
      { "/documents/d-gone": { status: 404, body: { code: "document.not_found" } } },
      { path: "/documents/d-gone" },
    );

    expect(
      await screen.findByRole("heading", { level: 2, name: "This document isn't available" }),
    ).toBeVisible();
    await expectNoAxeViolations();
    await userEvent.click(screen.getByRole("link", { name: "Back to documents" }));

    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus(),
    );
  });

  it("selects the establishment of a document opened by URL", async () => {
    renderDocuments(
      {
        "/documents/d-sp": {
          body: { ...processing, id: "d-sp", establishmentId: "e-sao-paulo", title: "POP" },
        },
        "/establishments/e-sao-paulo/documents": { body: [] },
      },
      { path: "/documents/d-sp" },
    );

    expect(await screen.findByText("SOP · Bistro São Paulo")).toBeVisible();
    await waitFor(() =>
      expect(latestHub().invoke).toHaveBeenLastCalledWith("JoinEstablishment", "e-sao-paulo"),
    );
  });

  it("is laid out for 375px without violations", async () => {
    renderDocuments({}, { path: "/documents/d-cold", width: 375 });

    await screen.findByText("Version 4 is processing.");
    await expectNoAxeViolations();
  });
});

describe("expiry dates", () => {
  const expired = dayFromToday(-3);
  const soon = dayFromToday(10);
  const later = dayFromToday(90);
  const dated = [
    {
      ...fields("d-license", "Food business license", "License", 1, expired),
      latestVersion: version(1, "Indexed"),
    },
    {
      ...fields("d-pest", "Pest control certificate", "Other", 1, soon),
      latestVersion: version(1, "Indexed"),
    },
    {
      ...fields("d-water", "Water analysis", "Report", 1, later),
      latestVersion: version(1, "Indexed"),
    },
    list[0],
  ];
  const datedPath = "/establishments/e-lisboa/documents";
  const filter = () => screen.getByRole("combobox", { name: "Filter by expiry" });
  const patches = (fetch: ReturnType<typeof stubApi>) =>
    calls(fetch, "/establishments/e-lisboa/documents/d-cold", "PATCH").map(
      ([, init]) => JSON.parse(init?.body as string) as unknown,
    );

  it("marks expired and expiring documents, and shows a later date as a date", async () => {
    renderDocuments({ [datedPath]: { body: dated } });

    expect(
      await screen.findByRole("link", { name: "Food business license" }),
    ).toHaveAccessibleDescription("Indexed Expired");
    expect(
      screen.getByRole("link", { name: "Pest control certificate" }),
    ).toHaveAccessibleDescription("Indexed Expires in 10 days");
    // A date more than 30 days away isn't urgent: the row reads as before.
    expect(screen.getByRole("link", { name: "Water analysis" })).toHaveAccessibleDescription(
      "Indexed",
    );
    expect(screen.getByRole("link", { name: "Cold storage SOP" })).toHaveAccessibleDescription(
      "Indexed",
    );
    expect(within(card()).getByRole("columnheader", { name: "Expires" })).toBeInTheDocument();
    const [license, pest, water, sop] = rows();
    expect(within(license).getAllByText("Expired")[0]).toBeVisible();
    expect(within(pest).getAllByText("Expires in 10 days")[0]).toHaveAttribute("datetime", soon);
    expect(within(water).getByText(calendarDate(later))).toHaveAttribute("datetime", later);
    expect(within(sop).getByText("No expiry date")).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("filters with the pills' own rule, in the browser, and keeps the filter", async () => {
    // Expiring today: local "today", which the API's UTC filters would miss every evening west of UTC.
    const today = {
      ...fields("d-allergen", "Allergen certificate", "Other", 1, dayFromToday(0)),
      latestVersion: version(1, "Indexed"),
    };
    const fetch = renderDocuments({
      [datedPath]: { body: [...dated, today] },
      "/documents/d-license": {
        body: { ...dated[0], latestVersion: undefined, versions: [version(1, "Indexed")] },
      },
    });
    const user = userEvent.setup();
    const titles = () => rows().map((row) => within(row).getByRole("link").textContent);
    await screen.findByRole("link", { name: "Water analysis" });
    expect(filter()).toHaveValue("");
    expect(
      within(filter())
        .getAllByRole("option")
        .map((option) => option.textContent),
    ).toEqual(["All documents", "Expired", "Expiring in 30 days"]);

    await user.selectOptions(filter(), "expired");
    expect(titles()).toEqual(["Food business license"]);
    // The caption still counts every document.
    expect(within(card()).getByText("5 documents")).toBeVisible();

    // Opening a document and coming back keeps the filter.
    await user.click(screen.getByRole("link", { name: "Food business license" }));
    await screen.findByRole("heading", { level: 2, name: "Food business license" });
    await user.click(within(screen.getByRole("main")).getByRole("link", { name: "Documents" }));
    expect(await screen.findByRole("link", { name: "Food business license" })).toBeVisible();
    expect(filter()).toHaveValue("expired");

    await user.selectOptions(filter(), "expiring");
    expect(titles()).toEqual(["Pest control certificate", "Allergen certificate"]);
    await expectNoAxeViolations();
    expect(fetch.mock.calls.some(([input]) => input.includes("?"))).toBe(false);
  });

  it("says when nothing matches, and Show all documents clears the filter", async () => {
    renderDocuments();
    const user = userEvent.setup();
    await screen.findByRole("link", { name: "Cold storage SOP" });

    await user.selectOptions(filter(), "expired");
    expect(within(card()).getByText("No expired documents")).toBeVisible();
    expect(within(card()).queryByRole("table")).toBeNull();
    await expectNoAxeViolations();

    await user.click(within(card()).getByRole("button", { name: "Show all documents" }));
    expect(screen.getByRole("heading", { level: 2, name: "Bistro Lisboa" })).toHaveFocus();
    expect(filter()).toHaveValue("");
    expect(rows()).toHaveLength(4);
  });

  it("shows the expired and expiring badges under the title below 768px", async () => {
    renderDocuments({ [datedPath]: { body: dated } }, { width: 375 });

    await screen.findByRole("link", { name: "Food business license" });
    await expectNoAxeViolations();
  });

  it("uploads with an expiry date, refuses a half-typed one, and clears the filter", async () => {
    const fetch = renderDocuments({
      "POST /establishments/e-lisboa/documents": {
        status: 202,
        body: {
          ...fields("d-new", "License", "License", null, soon),
          versions: [version(1, "Uploaded")],
        },
      },
    });
    const user = userEvent.setup();
    await screen.findByRole("link", { name: "Cold storage SOP" });
    await user.selectOptions(filter(), "expired");
    await user.click(screen.getByRole("button", { name: "Upload document" }));
    const dialog = await screen.findByRole("dialog", { name: "Upload document" });
    await user.upload(fileInput(), pdf("license.pdf"));
    await user.selectOptions(within(dialog).getByRole("combobox", { name: "Type" }), "License");
    const date = within(dialog).getByLabelText("Expiry date");
    expect(date).toHaveAccessibleDescription(
      "For licenses and certificates. Leave empty if the document doesn't expire.",
    );

    // The browser reads a half-typed date as "", which would upload without one.
    Object.defineProperty(date, "validity", { configurable: true, value: { badInput: true } });
    await user.click(within(dialog).getByRole("button", { name: "Upload" }));
    await waitFor(() => expect(date).toHaveFocus());
    expect(date).toHaveAccessibleDescription(/Enter a complete date, or leave the field empty\.$/);
    expect(calls(fetch, "/establishments/e-lisboa/documents", "POST")).toHaveLength(0);
    await expectNoAxeViolations();

    Object.defineProperty(date, "validity", { configurable: true, value: { badInput: false } });
    fireEvent.change(date, { target: { value: soon } });
    await user.click(within(dialog).getByRole("button", { name: "Upload" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(lastForm(fetch).get("expiresOn")).toBe(soon);
    // The new document is seen, whatever its date.
    expect(filter()).toHaveValue("");
  });

  it("edits and removes a document's expiry date", async () => {
    let server = { ...indexed, expiresOn: soon };
    const fetch = renderDocuments(
      {
        "/documents/d-cold": () => Promise.resolve(Response.json(server)),
        "PATCH /establishments/e-lisboa/documents/d-cold": () => {
          const [, init] = fetch.mock.calls.at(-1)!;
          server = { ...server, ...(JSON.parse(init?.body as string) as object) };
          return Promise.resolve(new Response(null, { status: 204 }));
        },
      },
      { path: "/documents/d-cold" },
    );
    const user = userEvent.setup();
    expect(await screen.findByText(`Expires ${calendarDate(soon)}`)).toBeVisible();
    expect(screen.getByText("Expires in 10 days")).toBeVisible();

    const edit = screen.getByRole("button", { name: "Edit expiry date" });
    await user.click(edit);
    let dialog = await screen.findByRole("dialog", { name: "Edit expiry date" });
    const date = within(dialog).getByLabelText("Expiry date");
    expect(date).toHaveValue(soon);
    // Nothing changed yet.
    expect(within(dialog).getByRole("button", { name: "Save" })).toHaveAttribute(
      "aria-disabled",
      "true",
    );
    await expectNoAxeViolations();

    const next = dayFromToday(40);
    fireEvent.change(date, { target: { value: next } });
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(edit).toHaveFocus();
    expect(screen.getByText("Expiry date saved.")).toBeInTheDocument();
    expect(await screen.findByText(`Expires ${calendarDate(next)}`)).toBeVisible();
    expect(screen.queryByText(/^Expires in/)).toBeNull();

    await user.click(edit);
    dialog = await screen.findByRole("dialog", { name: "Edit expiry date" });
    // Remove date only empties the field; Save sends it.
    await user.click(within(dialog).getByRole("button", { name: "Remove date" }));
    expect(within(dialog).getByLabelText("Expiry date")).toHaveValue("");
    expect(within(dialog).getByLabelText("Expiry date")).toHaveFocus();
    expect(within(dialog).queryByRole("button", { name: "Remove date" })).toBeNull();
    expect(patches(fetch)).toHaveLength(1);
    await user.click(within(dialog).getByRole("button", { name: "Save" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(edit).toHaveFocus();
    expect(screen.getByText("Expiry date removed.")).toBeInTheDocument();
    expect(await screen.findByText("No expiry date")).toBeVisible();
    expect(patches(fetch)).toEqual([{ expiresOn: next }, { expiresOn: null }]);
  });

  it("shows the date, without the edit action, to who can't upload", async () => {
    renderDocuments(
      { "/documents/d-cold": { body: { ...indexed, expiresOn: expired } } },
      { path: "/documents/d-cold", permissions: ["establishments.read", "documents.read"] },
    );

    expect(await screen.findByText(`Expires ${calendarDate(expired)}`)).toBeVisible();
    expect(screen.getByText("Expired")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Edit expiry date" })).toBeNull();
    await expectNoAxeViolations();
  });
});

describe("documents access and navigation", () => {
  it("goes to the list when the establishment changes on a document", async () => {
    renderDocuments(
      { "/establishments/e-sao-paulo/documents": { body: [] } },
      {
        path: "/documents/d-cold",
      },
    );
    await screen.findByText("Version 4 is processing.");
    const user = userEvent.setup();

    const switcher = screen.getByRole("combobox", { name: "Establishment" });
    switcher.focus();
    await user.keyboard("{Enter}");
    await screen.findByRole("listbox");
    await user.keyboard("{ArrowDown}{Enter}");

    await waitFor(() => expect(window.location.pathname).toBe("/documents"));
    expect(await screen.findByRole("region", { name: "Bistro São Paulo" })).toBeVisible();
    expect(screen.getByText("Bistro São Paulo selected")).toBeInTheDocument();
    await waitFor(() =>
      expect(latestHub().invoke).toHaveBeenLastCalledWith("JoinEstablishment", "e-sao-paulo"),
    );
    expect(hubs.all).toHaveLength(1);
  });

  it("opens the first establishment's documents when none is remembered", async () => {
    renderDocuments({}, { remembered: false });

    expect(await screen.findByRole("heading", { level: 2, name: "Bistro Lisboa" })).toBeVisible();
    expect(screen.queryByRole("region", { name: "Establishments" })).toBeNull();
  });

  it("refuses Documents without documents.read and never asks for documents", async () => {
    const fetch = renderDocuments({}, { permissions: ["establishments.read", "chat.ask"] });

    expect(
      await screen.findByRole("heading", { level: 2, name: "You don't have access to Documents" }),
    ).toBeVisible();
    expect(screen.queryByRole("link", { name: "Documents" })).toBeNull();
    expect(fetch.mock.calls.some(([input]) => input.includes("/documents"))).toBe(false);
    expect(hubs.all).toHaveLength(0);
    await expectNoAxeViolations();
  });
});
