import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { App } from "../App";
import { createAuth } from "../auth/session";
import {
  establishments,
  expectNoAxeViolations,
  fakeOidc,
  me,
  mockViewport,
  sseResponse,
  stubApi,
} from "../test/fakes";

const storageKey = `normetry.establishment.${me.tenantId}.${me.userId}`;
const never = () => new Promise<Response>(() => {});
const regulation = "Regulation (EC) 852/2004";

const passage =
  "5. Raw materials, ingredients and finished products likely to support the reproduction of pathogenic micro-organisms are not to be kept at temperatures that might result in a risk to health.";
const quote = "not to be kept at temperatures that might result in a risk to health";

const citation = (id: string, sectionPath: string, overrides: object = {}) => ({
  id,
  sourceTitle: "Regulation (EC) No 852/2004 on the hygiene of foodstuffs",
  sourceVersion: "2009-04-20",
  language: "en",
  sectionPath,
  text: passage,
  quote,
  quoteStart: passage.indexOf(quote),
  quoteEnd: passage.indexOf(quote) + quote.length,
  officialUrl: "https://eur-lex.europa.eu/eli/reg/2004/852/oj",
  ...overrides,
});

const claim1 = {
  text: "Keep raw materials out of temperatures that risk health.",
  citations: [citation("cit-1", "Annex II, Chapter IX")],
};
const claim2 = {
  text: "Do not interrupt the cold chain.",
  citations: [citation("cit-2", "Article 4")],
};

const question = "How cold must raw materials be kept?";
const summary = (id: string, establishmentId = "e-lisboa", title: string | null = question) => ({
  id,
  establishmentId,
  title,
  updatedAt: "2026-11-13T09:30:00+00:00",
});
const asked = { id: "m1", role: "user", text: question, createdAt: "" };
const answered = (claims: object[]) => ({
  id: "m2",
  role: "assistant",
  kind: "answer",
  language: "en",
  claims,
  createdAt: "",
});
const saved = { ...summary("c-1"), messages: [asked, answered([claim1, claim2])] };

const createRoute = "POST /ai/establishments/e-lisboa/conversations";
const messagesRoute = (id: string) =>
  `POST /ai/establishments/e-lisboa/conversations/${id}/messages`;
const quota = { limit: 20, remaining: 12, resetsAt: "2026-11-14T00:00:00Z" };

function renderChat(routes: Parameters<typeof stubApi>[0] = {}, { path = "/", width = 1280 } = {}) {
  mockViewport(width);
  localStorage.setItem(storageKey, "e-lisboa");
  window.history.replaceState(null, "", path);
  const fetch = stubApi({
    "/me": { body: me },
    "/establishments": { body: establishments },
    "/ai/conversations": { body: [] },
    "/chat/quota": { body: quota },
    ...routes,
  });
  render(<App auth={createAuth(fakeOidc().oidc)} />);
  return fetch;
}

const composer = () => screen.findByRole("textbox", { name: "Your question" });
const messages = () => screen.getByRole("list", { name: "Messages" });
const conversation = () => screen.getByRole("region", { name: /./ });
const liveRegion = () => document.querySelector("[aria-live='polite'] p")!.parentElement!;

async function ask(text = question) {
  await userEvent.type(await composer(), `${text}{Enter}`);
}

/** A new conversation whose first answer streams from the returned response. */
function newConversation(stream = sseResponse()) {
  return {
    stream,
    routes: {
      [createRoute]: { status: 201, body: summary("c-new", "e-lisboa", null) },
      [messagesRoute("c-new")]: () => Promise.resolve(stream.response),
    },
  };
}

describe("new conversation", () => {
  it("shows the jurisdiction, the language rule and the quota", async () => {
    renderChat();

    expect(await screen.findByRole("heading", { name: `Ask about ${regulation}` })).toBeVisible();
    expect(
      screen.getByText(
        "Ask in English or Portuguese. Sources are quoted in the language you ask in.",
      ),
    ).toBeVisible();
    expect(screen.getByRole("heading", { level: 2, name: "New conversation" })).toBeVisible();
    expect(screen.getByText(`Bistro Lisboa · ${regulation}`)).toBeVisible();
    expect(await screen.findByText("12 of 20 messages left today")).toBeVisible();
    expect(await screen.findByText("No conversations yet")).toBeVisible();
    expect(screen.getByRole("button", { name: "Send" })).toHaveAttribute("aria-disabled", "true");
    await expectNoAxeViolations();
  });

  it("does not send an empty question, and Shift+Enter breaks the line", async () => {
    const fetch = renderChat();
    const field = await composer();

    await userEvent.type(field, "   {Enter}");
    await userEvent.type(field, "{Shift>}{Enter}{/Shift}");

    expect(fetch).not.toHaveBeenCalledWith(
      expect.stringContaining("/conversations"),
      expect.objectContaining({ method: "POST" }),
    );
    expect(field).toHaveValue("   \n");
  });
});

describe("a streamed answer", () => {
  it("creates the conversation on the first send and shows claims in order as they arrive", async () => {
    const { stream, routes } = newConversation(
      sseResponse({
        "RateLimit-Limit": "20",
        "RateLimit-Remaining": "11",
        "RateLimit-Reset": "60",
      }),
    );
    const fetch = renderChat(routes);

    await ask();

    await waitFor(() => expect(window.location.pathname).toBe("/conversations/c-new"));
    expect(within(messages()).getByText(question)).toBeVisible();
    expect(liveRegion()).toHaveTextContent(`Checking ${regulation}…`);
    expect(await composer()).toHaveValue("");
    expect(await composer()).toHaveFocus();
    const row = within(screen.getByRole("navigation", { name: "Conversations" })).getByRole(
      "link",
      { name: /How cold must raw materials be kept\?/ },
    );
    expect(row).toHaveAttribute("aria-current", "page");
    expect(
      fetch.mock.calls.find(([url]) => url.endsWith("/conversations/c-new/messages"))?.[1],
    ).toMatchObject({ method: "POST", body: JSON.stringify({ text: question }) });
    expect(await screen.findByText("11 of 20 messages left today")).toBeVisible();

    stream.send("claim", claim1);
    expect(await within(messages()).findByText(claim1.text)).toBeInTheDocument();
    expect(within(messages()).queryByText(claim2.text)).toBeNull();
    const answer = within(messages()).getAllByRole("listitem")[1];
    expect(answer).toHaveAttribute("aria-busy", "true");
    expect(screen.getByRole("button", { name: "Send" })).toHaveAttribute("aria-disabled", "true");
    expect(screen.getByRole("button", { name: "Delete conversation" })).toHaveAttribute(
      "aria-disabled",
      "true",
    );
    await expectNoAxeViolations();

    stream.send("claim", claim2);
    stream.send("done", {});
    stream.close();

    expect(await within(messages()).findByText(claim2.text)).toBeInTheDocument();
    await waitFor(() => expect(answer).toHaveAttribute("aria-busy", "false"));
    expect(answer.textContent.indexOf(claim1.text)).toBeLessThan(
      answer.textContent.indexOf(claim2.text),
    );
    expect(liveRegion()).toHaveTextContent(claim1.text);
    expect(liveRegion()).toHaveTextContent("Answer complete.");
    expect(
      await within(answer).findByRole("button", { name: "View source · Article 4" }),
    ).toHaveAttribute("aria-expanded", "false");
    expect(
      within(answer).getByRole("button", { name: "View source · Annex II, Chapter IX" }),
    ).toBeVisible();
    await expectNoAxeViolations();
  });

  it("marks a Portuguese answer in the English interface as Portuguese", async () => {
    const { stream, routes } = newConversation();
    renderChat(routes);
    await ask("Qual a temperatura para matérias-primas?");
    const claim = {
      text: "Mantenha as matérias-primas fora de temperaturas de risco.",
      citations: [citation("cit-pt", "Anexo II, Capítulo IX", { language: "pt" })],
      language: "pt",
    };

    stream.send("claim", claim);
    stream.send("done", {});

    await within(messages()).findByRole("button", { name: "View source · Anexo II, Capítulo IX" });
    expect(within(messages()).getByText(claim.text)).toHaveAttribute("lang", "pt");
    expect(within(liveRegion()).getByText(claim.text)).toHaveAttribute("lang", "pt");
    expect(await within(liveRegion()).findByText("Answer complete.")).not.toHaveAttribute("lang");
  });

  it("renders a refusal as one message", async () => {
    const { stream, routes } = newConversation();
    renderChat(routes);
    await ask();

    stream.send("refusal", { reason: "low_score" });

    const refusal = await within(messages()).findByText(
      `There isn't enough basis in ${regulation} to answer this question.`,
    );
    expect(refusal).toBeVisible();
    expect(
      within(messages()).getByText("Try rephrasing it or asking about a specific requirement."),
    ).toBeVisible();
    expect(within(messages()).queryByRole("button")).toBeNull();
    await expectNoAxeViolations();
  });

  it("never shows a claim without a citation, nor unknown events", async () => {
    const { stream, routes } = newConversation();
    renderChat(routes);
    await ask();

    stream.send("claim", { text: "An unchecked statement.", citations: [] });
    stream.send("note", { text: "Free text from the model." });
    stream.send("done", {});

    expect(
      await within(messages()).findByText(
        `There isn't enough basis in ${regulation} to answer this question.`,
      ),
    ).toBeVisible();
    expect(screen.queryByText("An unchecked statement.")).toBeNull();
    expect(screen.queryByText("Free text from the model.")).toBeNull();
  });

  it("shows unavailable with Try again, which retries in place", async () => {
    const first = sseResponse();
    const second = sseResponse();
    renderChat({
      [createRoute]: { status: 201, body: summary("c-new", "e-lisboa", null) },
      [messagesRoute("c-new")]: [
        () => Promise.resolve(first.response),
        () => Promise.resolve(second.response),
      ],
    });
    await ask();

    first.send("unavailable", {});

    const alert = await within(messages()).findByRole("alert");
    expect(alert).toHaveTextContent("The assistant is unavailable right now.");
    expect(alert).toHaveTextContent("Try again in a few minutes.");
    expect(alert).toHaveTextContent("Trying again uses one of today's messages.");
    await expectNoAxeViolations();

    await userEvent.click(within(alert).getByRole("button", { name: "Try again" }));
    expect(await composer()).toHaveFocus();
    second.send("claim", claim1);
    second.send("done", {});

    expect(await within(messages()).findByText(claim1.text)).toBeInTheDocument();
    expect(within(messages()).queryByRole("alert")).toBeNull();
    expect(within(messages()).getAllByText(question)).toHaveLength(1);
  });

  it("shows a gateway 5xx as unavailable", async () => {
    renderChat({
      [createRoute]: { status: 201, body: summary("c-new", "e-lisboa", null) },
      [messagesRoute("c-new")]: { status: 502 },
    });
    await ask();

    expect(await within(messages()).findByRole("alert")).toHaveTextContent(
      "The assistant is unavailable right now.",
    );
  });

  it("shows a network error before any claim", async () => {
    renderChat({
      [createRoute]: { status: 201, body: summary("c-new", "e-lisboa", null) },
      [messagesRoute("c-new")]: () => Promise.reject(new TypeError("Failed to fetch")),
    });
    await ask();

    const alert = await within(messages()).findByRole("alert");
    expect(alert).toHaveTextContent(
      "Couldn't reach Normetry. Check your connection and try again.",
    );
    expect(within(alert).getByRole("button", { name: "Try again" })).toBeVisible();
    await expectNoAxeViolations();
  });

  it("keeps the claims when the connection drops mid-answer", async () => {
    const { stream, routes } = newConversation();
    renderChat(routes);
    await ask();

    stream.send("claim", claim1);
    stream.close();

    expect(await within(messages()).findByRole("alert")).toHaveTextContent(
      "The connection dropped before the answer finished.",
    );
    expect(within(messages()).getByText(claim1.text)).toBeInTheDocument();
  });

  it("shows a generic message for another 4xx", async () => {
    renderChat({ [createRoute]: { status: 400 } });
    await ask();

    expect(await within(messages()).findByRole("alert")).toHaveTextContent(
      "Couldn't get an answer. Try again.",
    );
  });

  it("on 429 takes the question back to the composer and shows the quota notice", async () => {
    renderChat({
      [createRoute]: { status: 201, body: summary("c-new", "e-lisboa", null) },
      [messagesRoute("c-new")]: { status: 429, headers: { "Retry-After": "21600" } },
    });
    await ask();

    expect(
      await within(conversation()).findByText("You've used all of today's messages"),
    ).toBeVisible();
    expect(
      within(conversation()).getByText(
        "Your plan includes 20 messages a day. You can ask again in 6 hours.",
      ),
    ).toBeVisible();
    expect(await composer()).toHaveValue(question);
    expect(screen.queryByRole("list", { name: "Messages" })).toBeNull();
    expect(screen.getByText("0 of 20 messages left today")).toBeVisible();
    const send = screen.getByRole("button", { name: "Send" });
    expect(send).toHaveAttribute("aria-disabled", "true");
    expect(send).toHaveAccessibleDescription(/You've used all of today's messages/);
    expect(liveRegion()).toHaveTextContent("You've used all of today's messages");
    await expectNoAxeViolations();
  });

  it("aborts the answer and starts over when the establishment changes", async () => {
    const { routes } = newConversation();
    const fetch = renderChat(routes);
    await ask();
    await waitFor(() => expect(window.location.pathname).toBe("/conversations/c-new"));
    const user = userEvent.setup();

    const switcher = screen.getByRole("combobox", { name: "Establishment" });
    switcher.focus();
    await user.keyboard("{Enter}");
    await screen.findByRole("listbox");
    await user.keyboard("{ArrowDown}{Enter}");

    await waitFor(() => expect(window.location.pathname).toBe("/"));
    const call = fetch.mock.calls.find(([url]) => url.endsWith("/messages"));
    expect(call?.[1]?.signal?.aborted).toBe(true);
    expect(screen.getByText("Bistro São Paulo · Anvisa RDC 216/2004")).toBeVisible();
    expect(screen.getByRole("heading", { level: 2, name: "New conversation" })).toBeVisible();
  });
});

describe("a saved conversation", () => {
  it("shows skeleton turns while it loads", async () => {
    renderChat(
      { "/ai/conversations": { body: [summary("c-1")] }, "/ai/conversations/c-1": never },
      {
        path: "/conversations/c-1",
      },
    );

    const region = await screen.findByRole("region", { name: question });
    expect(region).toHaveAttribute("aria-busy", "true");
    expect(within(region).getByText("Loading conversation…")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Send" })).toHaveAttribute("aria-disabled", "true");
    await expectNoAxeViolations();
  });

  it("shows stored answers whole, with their chips", async () => {
    renderChat(
      { "/ai/conversations": { body: [summary("c-1")] }, "/ai/conversations/c-1": { body: saved } },
      { path: "/conversations/c-1" },
    );

    expect(
      await within(await screen.findByRole("list", { name: "Messages" })).findByText(claim2.text),
    ).toBeVisible();
    expect(
      screen.getByRole("button", { name: "View source · Annex II, Chapter IX" }),
    ).toBeVisible();
    expect(conversation()).toHaveAttribute("aria-busy", "false");
    expect(within(messages()).getByText(claim1.text)).toHaveAttribute("lang", "en");
    expect(
      within(screen.getByRole("navigation", { name: "Conversations" })).getByRole("link", {
        name: /How cold/,
      }),
    ).toHaveAttribute("aria-current", "page");
    await expectNoAxeViolations();
  });

  it("shows a load error with Try again", async () => {
    renderChat(
      { "/ai/conversations/c-1": [{ status: 503 }, { status: 503 }, { body: saved }] },
      { path: "/conversations/c-1" },
    );

    const alert = await screen.findByRole("alert", {}, { timeout: 3000 });
    expect(alert).toHaveTextContent("Couldn't load this conversation");
    await expectNoAxeViolations();
    await userEvent.click(within(alert).getByRole("button", { name: "Try again" }));

    expect(await screen.findByRole("list", { name: "Messages" })).toBeVisible();
    expect(screen.getByRole("heading", { level: 2, name: question })).toHaveFocus();
  });

  it("shows not found, and starts a new conversation from there", async () => {
    renderChat({ "/ai/conversations/c-gone": { status: 404 } }, { path: "/conversations/c-gone" });

    expect(await screen.findByText("This conversation isn't available")).toBeVisible();
    expect(screen.getByText("It may have been deleted.")).toBeVisible();
    await expectNoAxeViolations();
    await userEvent.click(screen.getByRole("button", { name: "Start a new conversation" }));

    expect(window.location.pathname).toBe("/");
    expect(await composer()).toHaveFocus();
  });

  it("says so when the conversation is gone on send, and focuses the way out", async () => {
    renderChat(
      {
        "/ai/conversations": { body: [summary("c-1")] },
        "/ai/conversations/c-1": { body: saved },
        [messagesRoute("c-1")]: { status: 404 },
      },
      { path: "/conversations/c-1" },
    );
    await screen.findByRole("list", { name: "Messages" });

    await ask("And frozen food?");

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("This conversation isn't available");
    await waitFor(() =>
      expect(within(alert).getByRole("button", { name: "Start a new conversation" })).toHaveFocus(),
    );
    await expectNoAxeViolations();
  });

  it.each([
    ["from the list", { body: [summary("c-sp", "e-sao-paulo")] }],
    ["from the conversation when the list fails", { status: 404 }],
  ])("selects the establishment of a conversation opened by URL, %s", async (_case, list) => {
    renderChat(
      {
        "/ai/conversations": list,
        "/ai/conversations/c-sp": { body: { ...saved, ...summary("c-sp", "e-sao-paulo") } },
      },
      { path: "/conversations/c-sp" },
    );

    expect(await screen.findByText("Bistro São Paulo · Anvisa RDC 216/2004")).toBeVisible();
    expect(
      await within(await screen.findByRole("list", { name: "Messages" })).findByText(claim1.text),
    ).toBeVisible();
  });

  it("deletes after confirmation and goes to a new conversation", async () => {
    renderChat(
      {
        "/ai/conversations": [{ body: [summary("c-1")] }, { body: [] }],
        "/ai/conversations/c-1": { body: saved },
        "DELETE /ai/conversations/c-1": { status: 204 },
      },
      { path: "/conversations/c-1" },
    );
    await screen.findByRole("list", { name: "Messages" });

    await userEvent.click(screen.getByRole("button", { name: "Delete conversation" }));

    const dialog = await screen.findByRole("alertdialog", { name: "Delete this conversation?" });
    expect(dialog).toHaveAccessibleDescription(
      `“${question}” and its answers will be deleted. This can't be undone.`,
    );
    expect(within(dialog).getByRole("button", { name: "Cancel" })).toHaveFocus();
    await expectNoAxeViolations();
    await userEvent.click(within(dialog).getByRole("button", { name: "Delete" }));

    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(window.location.pathname).toBe("/");
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: "New conversation" })).toHaveFocus(),
    );
    expect(liveRegion()).toHaveTextContent("Conversation deleted.");
    expect(screen.queryByRole("link", { name: /How cold/ })).toBeNull();
  });

  it("keeps the dialog open when the delete fails", async () => {
    renderChat(
      {
        "/ai/conversations": { body: [summary("c-1")] },
        "/ai/conversations/c-1": { body: saved },
        "DELETE /ai/conversations/c-1": { status: 503 },
      },
      { path: "/conversations/c-1" },
    );
    await screen.findByRole("list", { name: "Messages" });
    await userEvent.click(screen.getByRole("button", { name: "Delete conversation" }));
    const dialog = await screen.findByRole("alertdialog");

    await userEvent.click(within(dialog).getByRole("button", { name: "Delete" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "Couldn't delete the conversation. Try again.",
    );
    expect(window.location.pathname).toBe("/conversations/c-1");
    await expectNoAxeViolations();
  });
});

describe("source panel", () => {
  const openSaved = (width: number, conversationOverride = saved) =>
    renderChat(
      {
        "/ai/conversations": { body: [summary("c-1")] },
        "/ai/conversations/c-1": { body: conversationOverride },
      },
      { path: "/conversations/c-1", width },
    );

  it("opens inline from 1280px with the quote highlighted, and closes with Esc", async () => {
    openSaved(1280);
    const chip = await screen.findByRole("button", { name: "View source · Annex II, Chapter IX" });

    await userEvent.click(chip);

    const panel = screen.getByRole("complementary", { name: "Source" });
    expect(chip).toHaveAttribute("aria-expanded", "true");
    expect(chip).toHaveAttribute("aria-controls", panel.id);
    expect(within(panel).getByRole("heading", { name: "Source" })).toHaveFocus();
    expect(panel.querySelector("mark")).toHaveTextContent(quote);
    expect(panel).toHaveTextContent("Quoted passage:");
    expect(within(panel).getByText("Version 2009-04-20 · English")).toBeVisible();
    expect(within(panel).getByText(passage.slice(0, 20), { exact: false })).toHaveAttribute(
      "lang",
      "en",
    );
    const link = within(panel).getByRole("link", { name: /Open the official page/ });
    expect(link).toHaveAttribute("href", "https://eur-lex.europa.eu/eli/reg/2004/852/oj");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
    await expectNoAxeViolations();

    await userEvent.keyboard("{Escape}");

    expect(screen.queryByRole("complementary", { name: "Source" })).toBeNull();
    expect(chip).toHaveFocus();
    expect(chip).toHaveAttribute("aria-expanded", "false");
  });

  it("opens in a sheet below 1280px and gives focus back to the chip", async () => {
    openSaved(1024);
    const chip = await screen.findByRole("button", { name: "View source · Article 4" });

    await userEvent.click(chip);

    const sheet = await screen.findByRole("dialog", { name: "Source" });
    await waitFor(() =>
      expect(within(sheet).getByRole("heading", { name: "Source" })).toHaveFocus(),
    );
    expect(sheet.querySelector("mark")).toHaveTextContent(quote);
    await expectNoAxeViolations();
    await userEvent.click(within(sheet).getByRole("button", { name: "Close source" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() => expect(chip).toHaveFocus());
  });

  it("shows the quote apart when it cannot be located in the passage", async () => {
    const unmapped = {
      ...claim1,
      citations: [citation("cit-1", "Annex II, Chapter IX", { quoteStart: null, quoteEnd: null })],
    };
    openSaved(1280, {
      ...saved,
      messages: [asked, answered([unmapped])],
    });

    await userEvent.click(
      await screen.findByRole("button", { name: "View source · Annex II, Chapter IX" }),
    );

    const panel = screen.getByRole("complementary", { name: "Source" });
    expect(panel.querySelector("mark")).toBeNull();
    expect(within(panel).getByText("Quoted passage")).toBeVisible();
    expect(within(panel).getByText(quote)).toBeVisible();
  });
});

describe("conversation list", () => {
  it("shows skeleton rows while loading", async () => {
    renderChat({ "/ai/conversations": never });

    const list = await screen.findByRole("navigation", { name: "Conversations" });
    expect(list).toHaveAttribute("aria-busy", "true");
    expect(within(list).getByText("Loading conversations…")).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("shows the selected establishment's conversations, latest first", async () => {
    renderChat({
      "/ai/conversations": {
        body: [
          { ...summary("c-old"), title: "Older question", updatedAt: "2025-03-02T10:00:00Z" },
          summary("c-sp", "e-sao-paulo", "Pergunta em São Paulo"),
          { ...summary("c-new"), title: null, updatedAt: "2026-11-10T10:00:00Z" },
        ],
      },
    });

    const list = await screen.findByRole("navigation", { name: "Conversations" });
    const rows = await within(list).findAllByRole("link");
    expect(rows.map((row) => row.textContent)).toEqual([
      "Untitled conversationNov 10",
      "Older questionMar 2, 2025",
    ]);
    expect(within(list).queryByText("Pergunta em São Paulo")).toBeNull();
    await expectNoAxeViolations();
  });

  it("shows an error with Try again", async () => {
    renderChat({
      "/ai/conversations": [{ status: 503 }, { status: 503 }, { body: [summary("c-1")] }],
    });

    const alert = await screen.findByRole("alert", {}, { timeout: 3000 });
    expect(alert).toHaveTextContent("Couldn't load conversations");
    await expectNoAxeViolations();
    await userEvent.click(within(alert).getByRole("button", { name: "Try again" }));

    expect(await screen.findByRole("link", { name: /How cold/ })).toBeVisible();
    expect(screen.getByRole("heading", { level: 2, name: "Conversations" })).toHaveFocus();
  });

  it("is a sheet below 768px; a row opens and focuses the conversation", async () => {
    renderChat(
      {
        "/ai/conversations": { body: [summary("c-1")] },
        "/ai/conversations/c-1": { body: saved },
      },
      { width: 600 },
    );
    expect(screen.queryByRole("navigation", { name: "Conversations" })).toBeNull();

    await userEvent.click(await screen.findByRole("button", { name: "Show conversations" }));

    let sheet = await screen.findByRole("dialog", { name: "Conversations" });
    await expectNoAxeViolations();
    await userEvent.click(within(sheet).getByRole("button", { name: "Close conversations" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Show conversations" })).toHaveFocus(),
    );
    await userEvent.click(screen.getByRole("button", { name: "Show conversations" }));
    sheet = await screen.findByRole("dialog", { name: "Conversations" });
    await userEvent.click(await within(sheet).findByRole("link", { name: /How cold/ }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(window.location.pathname).toBe("/conversations/c-1");
    await waitFor(() =>
      expect(screen.getByRole("heading", { level: 2, name: question })).toHaveFocus(),
    );
  });
});
