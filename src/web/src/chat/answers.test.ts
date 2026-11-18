import { describe, expect, it } from "vitest";
import type { ConversationMessage } from "../api/queries";
import {
  quotaAfter,
  rateLimited,
  readEvents,
  streamAnswer,
  toClaim,
  toTurns,
  untilReset,
} from "./answers";

const citation = {
  id: "cit-1",
  sourceTitle: "FDA Food Code",
  sourceVersion: "2026",
  language: "en",
  sectionPath: "3-501.16",
  text: "Food shall be maintained at 5°C (41°F) or less.",
  quote: "maintained at 5°C (41°F) or less",
  quoteStart: 14,
  quoteEnd: 46,
  officialUrl: "https://www.fda.gov/food/fda-food-code",
};

/** A body delivered in exactly these chunks. */
function body(...chunks: string[]) {
  const encoder = new TextEncoder();
  return new ReadableStream<Uint8Array>({
    start(controller) {
      chunks.forEach((chunk) => controller.enqueue(encoder.encode(chunk)));
      controller.close();
    },
  });
}

async function collect(stream: ReadableStream<Uint8Array>, idleMs?: number) {
  const events = [];
  for await (const event of readEvents(stream, idleMs)) events.push(event);
  return events;
}

describe("readEvents", () => {
  it("parses events split anywhere across chunks, CRLF included", async () => {
    const events = await collect(
      body(
        ": ping\r\n\r",
        "\nevent: cl",
        'aim\r\ndata: {"a":\r\ndata:1}\r\n',
        "\r\nid: 7\nevent: done\ndata: {}\n\n",
        "event: claim\ndata: incomplete",
      ),
    );

    expect(events).toEqual([
      { event: "claim", data: '{"a":\n1}' },
      { event: "done", data: "{}" },
    ]);
  });

  it("gives up when nothing arrives in time", async () => {
    const silent = new ReadableStream<Uint8Array>();

    await expect(collect(silent, 10)).rejects.toThrow();
  });
});

describe("streamAnswer", () => {
  const response = (text: string) => new Response(body(text));

  it("passes valid claims in order and ends with the final event", async () => {
    const claims: string[] = [];
    const languages: (string | undefined)[] = [];
    const first = { text: "First.", citations: [citation], language: "pt" };
    const end = await streamAnswer(
      response(
        `event: claim\ndata: ${JSON.stringify(first)}\n\n` +
          `event: note\ndata: ${JSON.stringify({ text: "Free text." })}\n\n` +
          `event: claim\ndata: ${JSON.stringify({ text: "No source.", citations: [] })}\n\n` +
          `event: claim\ndata: not json\n\n` +
          `event: claim\ndata: ${JSON.stringify({ text: "Second.", citations: [citation] })}\n\n` +
          "event: done\ndata: {}\n\n",
      ),
      (claim, language) => {
        claims.push(claim.text);
        languages.push(language);
      },
    );

    expect(claims).toEqual(["First.", "Second."]);
    expect(languages).toEqual(["pt", undefined]);
    expect(end).toBe("done");
  });

  it("reports a stream that ends without a final event as lost", async () => {
    await expect(streamAnswer(response(": ping\n\n"), () => {})).resolves.toBe("network");
  });
});

/** A document citation as the AI service sends it (chat-api.md). */
const documentCitation = {
  id: "cit-doc",
  sourceType: "document",
  sourceTitle: null,
  sourceVersion: null,
  officialUrl: null,
  documentId: "d-1",
  documentTitle: "Cold holding SOP",
  documentType: "Sop",
  documentVersion: 3,
  language: "en",
  sectionPath: "Page 1",
  text: "The walk-in must read 4°C (39°F) or less at every check.",
  quote: "must read 4°C (39°F) or less",
  quoteStart: 12,
  quoteEnd: 40,
};
const parsedDocument = {
  id: "cit-doc",
  sourceType: "document",
  documentId: "d-1",
  documentTitle: "Cold holding SOP",
  documentType: "Sop",
  documentVersion: 3,
  language: "en",
  sectionPath: "Page 1",
  text: documentCitation.text,
  quote: documentCitation.quote,
  quoteStart: 12,
  quoteEnd: 40,
};

describe("toClaim", () => {
  it("keeps only well-formed citations and drops links that are not web pages", () => {
    const claim = toClaim({
      text: "Hold cold food at 5 °C or below.",
      citations: [
        { ...citation, officialUrl: "javascript:alert(1)", extra: "ignored" },
        { id: "cit-2" },
      ],
    });

    expect(claim).toEqual({
      text: "Hold cold food at 5 °C or below.",
      citations: [{ ...citation, sourceType: "regulation", officialUrl: null }],
    });
  });

  it("reads a citation without sourceType as the regulation's, and a labelled one as is", () => {
    const claim = toClaim({
      text: "Hold cold food at 5 °C or below.",
      citations: [citation, { ...citation, id: "cit-3", sourceType: "regulation" }],
    });

    expect(claim?.citations.map((c) => [c.id, c.sourceType])).toEqual([
      ["cit-1", "regulation"],
      ["cit-3", "regulation"],
    ]);
  });

  it("keeps a document citation with its document fields, and nothing of the regulation's", () => {
    expect(toClaim({ text: "The SOP says 4 °C.", citations: [documentCitation] })).toEqual({
      text: "The SOP says 4 °C.",
      citations: [parsedDocument],
    });
  });

  it("reads a missing or blank title or type as none", () => {
    const untitled = { ...documentCitation, documentTitle: " ", documentType: undefined };

    expect(toClaim({ text: "Untitled.", citations: [untitled] })?.citations).toEqual([
      { ...parsedDocument, documentTitle: null, documentType: null },
    ]);
  });

  it("drops a document citation without its id or version, and an unknown source type", () => {
    const claim = toClaim({
      text: "Unchecked.",
      citations: [
        { ...documentCitation, documentId: undefined },
        { ...documentCitation, documentVersion: "3" },
        { ...citation, sourceType: "web" },
        { ...citation, sourceType: "regulation", sourceTitle: null },
      ],
    });

    expect(claim).toBeNull();
  });

  it("rejects a claim with no citation or no text", () => {
    expect(toClaim({ text: "Unchecked.", citations: [] })).toBeNull();
    expect(toClaim({ text: " ", citations: [citation] })).toBeNull();
    expect(toClaim("text")).toBeNull();
  });
});

describe("toTurns", () => {
  it("pairs questions with their answers and keeps only valid claims", () => {
    const messages = [
      { id: "m1", role: "assistant", kind: "answer", language: "en", claims: [], createdAt: "" },
      { id: "m2", role: "user", text: "How cold?", createdAt: "" },
      {
        id: "m3",
        role: "assistant",
        kind: "answer",
        language: "pt",
        claims: [
          { text: "5 °C.", citations: [citation] },
          { text: "Unchecked.", citations: [] },
        ],
        createdAt: "",
      },
      { id: "m4", role: "user", text: "And hot?", createdAt: "" },
    ] as ConversationMessage[];

    expect(toTurns(messages)).toEqual([
      {
        key: "m2",
        question: "How cold?",
        status: "done",
        claims: [{ text: "5 °C.", citations: [{ ...citation, sourceType: "regulation" }] }],
        language: "pt",
        live: false,
        attempt: 0,
      },
      { key: "m4", question: "And hot?", claims: [], live: false, attempt: 0 },
    ]);
  });
});

describe("quota", () => {
  const now = Date.parse("2026-11-13T18:00:00Z");
  const known = { limit: 20, remaining: 5, resetsAt: "2026-11-14T00:00:00.000Z" };

  it("reads the RateLimit headers, and keeps what it knew without them", () => {
    const headers = {
      "RateLimit-Limit": "20",
      "RateLimit-Remaining": "4",
      "RateLimit-Reset": "60",
    };

    expect(quotaAfter(new Response(null, { headers }), known, now)).toEqual({
      limit: 20,
      remaining: 4,
      resetsAt: "2026-11-13T18:01:00.000Z",
    });
    expect(quotaAfter(new Response(null), known, now)).toBe(known);
  });

  it("knows none are left after a 429, until Retry-After", () => {
    const response = new Response(null, { status: 429, headers: { "Retry-After": "21600" } });

    expect(quotaAfter(response, undefined, now)).toEqual({
      limit: null,
      remaining: 0,
      resetsAt: "2026-11-14T00:00:00.000Z",
    });
  });

  it("tells the rate limit apart from the daily quota by the problem code", async () => {
    const problem = (status: number, code?: string) =>
      new Response(JSON.stringify({ status, code }), { status });

    expect(await rateLimited(problem(429, "rate_limited"))).toBe(true);
    expect(await rateLimited(problem(429, "chat.quota_exceeded"))).toBe(false);
    expect(await rateLimited(new Response(null, { status: 429 }))).toBe(false);
    expect(await rateLimited(problem(400, "rate_limited"))).toBe(false);
  });

  it("rounds the time until the reset up", () => {
    expect(untilReset(now + 5 * 3_600_000 + 60_000, now)).toBe("in 6 hours");
    expect(untilReset(now + 3_600_000, now)).toBe("in 1 hour");
    expect(untilReset(now + 61_000, now)).toBe("in 2 minutes");
    expect(untilReset(now + 1_000, now)).toBe("in 1 second");
  });
});
