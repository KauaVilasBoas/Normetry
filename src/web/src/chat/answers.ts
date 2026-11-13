import type { ChatQuota, Citation, Claim, ConversationMessage } from "../api/queries";
import { copy, format, locale } from "../copy";

/** How a turn's answer stands. `undefined` on a turn: no answer was stored. */
export type AnswerStatus =
  "pending" | "streaming" | "done" | "refusal" | "unavailable" | "network" | "error";

/** A question and its answer. */
export interface Turn {
  key: string;
  question: string;
  status?: AnswerStatus;
  /** Only claims that passed `toClaim`: each has text and at least one citation. */
  claims: Claim[];
  /** The claims' language (`pt` or `en`): the stored answer's, or the streamed claims'. */
  language?: string;
  /** Asked in the open view: its claims reveal, its failures alert and may be retried. */
  live: boolean;
  /** Bumped by Try again, so the reveal starts over. */
  attempt: number;
}

/** The DOM id of a citation's chip, to give it focus back when the source panel closes. */
export const chipId = (citation: Citation) => `citation-${citation.id}`;

const isString = (value: unknown): value is string => typeof value === "string";

function httpUrl(value: unknown): string | null {
  if (!isString(value)) return null;
  try {
    const url = new URL(value);
    return url.protocol === "https:" || url.protocol === "http:" ? url.href : null;
  } catch {
    return null;
  }
}

function toCitation(value: unknown): Citation | null {
  const c = value as Partial<Record<keyof Citation, unknown>> | null;
  if (
    !c ||
    !isString(c.id) ||
    !isString(c.sourceTitle) ||
    !isString(c.sourceVersion) ||
    !isString(c.language) ||
    !isString(c.sectionPath) ||
    !isString(c.text) ||
    !isString(c.quote)
  )
    return null;
  return {
    id: c.id,
    sourceTitle: c.sourceTitle,
    sourceVersion: c.sourceVersion,
    language: c.language,
    sectionPath: c.sectionPath,
    text: c.text,
    quote: c.quote,
    quoteStart: Number.isInteger(c.quoteStart) ? (c.quoteStart as number) : null,
    quoteEnd: Number.isInteger(c.quoteEnd) ? (c.quoteEnd as number) : null,
    // Only a web link may become an href; anything else (javascript:, data:) is dropped.
    officialUrl: httpUrl(c.officialUrl),
  };
}

/** A claim the screen may show: text and at least one well-formed citation, nothing else. */
export function toClaim(value: unknown): Claim | null {
  const c = value as { text?: unknown; citations?: unknown } | null;
  if (!c || !isString(c.text) || !c.text.trim() || !Array.isArray(c.citations)) return null;
  const citations = c.citations.map(toCitation).filter((citation) => citation !== null);
  return citations.length > 0 ? { text: c.text, citations } : null;
}

const storedStatus = { answer: "done", refusal: "refusal", unavailable: "unavailable" } as const;

/** Stored messages, oldest first, as turns. An answer without a question before it is skipped. */
export function toTurns(messages: ConversationMessage[]): Turn[] {
  const turns: Turn[] = [];
  for (const message of messages) {
    if (message.role === "user") {
      turns.push({ key: message.id, question: message.text, claims: [], live: false, attempt: 0 });
      continue;
    }
    const turn = turns.at(-1);
    if (message.role !== "assistant" || !turn || turn.status) continue;
    turn.status = storedStatus[message.kind] ?? "done";
    turn.claims = (Array.isArray(message.claims) ? message.claims : [])
      .map(toClaim)
      .filter((claim) => claim !== null);
    turn.language = message.language ?? undefined;
  }
  return turns;
}

/** No byte for this long, keep-alive comments included, and the answer counts as lost. */
export const IDLE_TIMEOUT_MS = 60_000;

class IdleTimeout extends Error {}

function readWithin<T>(read: Promise<T>, ms: number): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new IdleTimeout()), ms);
  });
  return Promise.race([read, timeout]).finally(() => clearTimeout(timer));
}

/**
 * Parses a `text/event-stream` body as it arrives (fields `event` and `data`; comments and other
 * fields are skipped). Throws when nothing arrives for `idleMs` or the stream fails.
 */
export async function* readEvents(
  body: ReadableStream<Uint8Array>,
  idleMs = IDLE_TIMEOUT_MS,
): AsyncGenerator<{ event: string; data: string }> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let event = "";
  let data: string[] = [];
  try {
    for (;;) {
      const { done, value } = await readWithin(reader.read(), idleMs);
      buffer += done ? decoder.decode() : decoder.decode(value, { stream: true });
      // A trailing \r may be half of a \r\n split across chunks: keep it for the next one.
      const end = !done && buffer.endsWith("\r") ? buffer.length - 1 : buffer.length;
      const lines = buffer.slice(0, end).split(/\r\n|\r|\n/);
      buffer = lines.pop()! + buffer.slice(end);
      for (const line of lines) {
        if (line === "") {
          if (data.length > 0) yield { event: event || "message", data: data.join("\n") };
          event = "";
          data = [];
        } else if (!line.startsWith(":")) {
          const colon = line.indexOf(":");
          const field = colon < 0 ? line : line.slice(0, colon);
          const raw = colon < 0 ? "" : line.slice(colon + 1);
          const fieldValue = raw.startsWith(" ") ? raw.slice(1) : raw;
          if (field === "event") event = fieldValue;
          else if (field === "data") data.push(fieldValue);
        }
      }
      // An event without its closing blank line is incomplete, and dropped.
      if (done) return;
    }
  } finally {
    reader.cancel().catch(() => {});
  }
}

export type StreamEnd = "done" | "refusal" | "unavailable" | "network";

function parse(data: string): unknown {
  try {
    return JSON.parse(data);
  } catch {
    return null;
  }
}

/**
 * Reads an answer stream: each valid claim goes to `onClaim` in order, with the answer's language
 * that every claim event carries. Resolves to the final event, or "network" when the stream
 * failed, went idle or ended without one. Unknown events are ignored, so nothing but claim text
 * and citations from the server reaches the screen.
 */
export async function streamAnswer(
  response: Response,
  onClaim: (claim: Claim, language: string | undefined) => void,
  idleMs = IDLE_TIMEOUT_MS,
): Promise<StreamEnd> {
  if (!response.body) return "network";
  try {
    for await (const { event, data } of readEvents(response.body, idleMs)) {
      if (event === "claim") {
        const value = parse(data);
        const claim = toClaim(value);
        const language = (value as { language?: unknown } | null)?.language;
        if (claim) onClaim(claim, isString(language) ? language : undefined);
      } else if (event === "done" || event === "refusal" || event === "unavailable") {
        return event;
      }
    }
  } catch {
    // A failed or idle stream: what arrived stands, the rest is lost.
  }
  return "network";
}

/** Seconds from a header, or undefined when absent or malformed. */
function seconds(headers: Headers, name: string): number | undefined {
  const value = headers.get(name);
  return value !== null && /^\d+$/.test(value) ? Number(value) : undefined;
}

const nextUtcMidnight = (now: number) => {
  const date = new Date(now);
  date.setUTCHours(24, 0, 0, 0);
  return date.toISOString();
};

/**
 * The quota after a `POST .../messages` response: a 429 means none left until `Retry-After`;
 * otherwise the `RateLimit-*` headers, when present, replace what was known.
 */
export function quotaAfter(
  response: Response,
  current: ChatQuota | undefined,
  now = Date.now(),
): ChatQuota | undefined {
  const at = (secs: number | undefined) =>
    secs === undefined ? undefined : new Date(now + secs * 1000).toISOString();
  if (response.status === 429) {
    return {
      limit: current?.limit ?? null,
      remaining: 0,
      resetsAt:
        at(seconds(response.headers, "Retry-After")) ?? current?.resetsAt ?? nextUtcMidnight(now),
    };
  }
  const limit = seconds(response.headers, "RateLimit-Limit");
  const remaining = seconds(response.headers, "RateLimit-Remaining");
  const resetsAt = at(seconds(response.headers, "RateLimit-Reset"));
  if (limit === undefined || remaining === undefined || resetsAt === undefined) return current;
  return { limit, remaining, resetsAt };
}

/** "in 6 hours" from one hour on, else "in 12 minutes"; rounded up so it is never too early. */
export function untilReset(resetsAt: number, now: number): string {
  const ms = Math.max(0, resetsAt - now);
  const relative = new Intl.RelativeTimeFormat(locale);
  return ms >= 3_600_000
    ? relative.format(Math.ceil(ms / 3_600_000), "hour")
    : relative.format(Math.max(1, Math.ceil(ms / 60_000)), "minute");
}

export function quotaNoticeText(quota: ChatQuota, now = Date.now()) {
  const when = untilReset(Date.parse(quota.resetsAt), now);
  return {
    title: copy.quota.reachedTitle,
    body:
      quota.limit === null
        ? format(copy.quota.reachedBodyNoLimit, { when })
        : format(copy.quota.reachedBody, { limit: String(quota.limit), when }),
  };
}
