import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState, type FocusEvent, type UIEvent } from "react";
import {
  conversationMessagesPath,
  conversationPath,
  conversationsKey,
  establishmentConversationsPath,
  quotaKey,
  useConversation,
  type ChatQuota,
  type Claim,
  type ConversationSummary,
} from "../api/chat";
import { ApiError, seconds } from "../api/client";
import { answerEvents } from "../api/contract";
import {
  meKey,
  queryFailed,
  useEstablishments,
  useQueryRetry,
  type Establishment,
} from "../api/queries";
import { refusalText, useSend } from "../api/send";
import { useSession } from "../auth/session";
import { copy, format } from "../copy";
import { focusIsLost } from "../ui/focus";
import {
  answering,
  quotaAfter,
  quotaNoticeText,
  rateLimited,
  streamAnswer,
  toTurns,
  untilReset,
  type Turn,
} from "./answers";

export const TITLE_ID = "conversation-title";
export const SHOW_CONVERSATIONS_ID = "show-conversations";
/** The conversation list's heading, which names its sheet below 768px. */
export const CONVERSATIONS_TITLE_ID = "conversations-title";

/** Follow new content only while the reader is this close to the bottom. */
const STICK_PX = 80;

export type ConversationView = "new" | "loading" | "error" | "not-found" | "ready";

/** A question being asked: its turn, its text, and the signal that aborts it. */
interface Asking {
  key: string;
  question: string;
  signal: AbortSignal;
}

/**
 * Keeps the latest content in view while the reader is at the bottom; leaves them if they
 * scrolled up. `scroller` and `onScroll` go on the scrolling area, `content` on the list (it
 * appears with the ready view); `follow()` sticks to the bottom again, for a new question.
 */
export function useStickToBottom() {
  const scroller = useRef<HTMLDivElement>(null);
  const [content, setContent] = useState<HTMLOListElement | null>(null);
  const stick = useRef(true);

  useEffect(() => {
    const area = scroller.current;
    if (!area || !content) return;
    const observer = new ResizeObserver(() => {
      if (stick.current) area.scrollTop = area.scrollHeight;
    });
    observer.observe(content);
    return () => observer.disconnect();
  }, [content]);

  const onScroll = ({ currentTarget: area }: UIEvent<HTMLDivElement>) => {
    stick.current = area.scrollHeight - area.scrollTop - area.clientHeight <= STICK_PX;
  };

  const follow = () => {
    stick.current = true;
  };

  return { scroller, content: setContent, onScroll, follow };
}

/** About 25 words per second (DESIGN.md §6). */
const WORD_MS = 40;

/**
 * A live answer's reveal: the words of each claim shown so far (`visible`, by claim), one more
 * every WORD_MS while `animate`, filling each claim before the next one starts; all of them
 * otherwise.
 */
export function useWordReveal(claims: Claim[], animate: boolean) {
  const words = claims.map((claim) => claim.text.split(" "));
  const total = words.reduce((sum, list) => sum + list.length, 0);
  const [shown, setShown] = useState(0);

  useEffect(() => {
    if (!animate || shown >= total) return;
    const next = setTimeout(() => setShown((count) => count + 1), WORD_MS);
    return () => clearTimeout(next);
  }, [animate, shown, total]);

  const visible: number[] = [];
  let left = animate ? shown : Infinity;
  for (const list of words) {
    visible.push(Math.min(list.length, left));
    left -= visible.at(-1)!;
  }
  return { words, visible };
}

/**
 * A rate limit holds Send until its end; `when` says it ("in 3 seconds"). It lasts seconds (at
 * most a minute): no countdown, the notice clears at its end.
 */
function useRateLimit() {
  const [limit, setLimit] = useState<{ until: number; when: string }>();

  useEffect(() => {
    if (!limit) return;
    const end = setTimeout(() => setLimit(undefined), Math.max(0, limit.until - Date.now()));
    return () => clearTimeout(end);
  }, [limit]);

  return { limitedWhen: limit?.when, hold: setLimit };
}

/**
 * The questions asked in this view and their streamed answers: the composer's draft and field,
 * Send and Try again, the conversation the first question creates (D4), the rate limit and the
 * daily quota, and a 404 on send. Unmounting aborts the answer in progress; the server stores
 * what was sent, and reopening shows it.
 */
export function useAnswerStream({
  establishment,
  conversationId,
  regulation,
  announce,
  onCreated,
  onAsk,
}: {
  establishment: Establishment;
  conversationId: string | undefined;
  regulation: string;
  announce: (text: string, lang?: string) => void;
  onCreated: (id: string) => void;
  /** A new question was asked (not a retry). */
  onAsk: () => void;
}) {
  const { api } = useSession();
  const queryClient = useQueryClient();
  const [createdId, setCreatedId] = useState<string>();
  const [turns, setTurns] = useState<Turn[]>([]);
  const [draft, setDraft] = useState("");
  const [notFound, setNotFound] = useState(false);
  const rateLimit = useRateLimit();
  const nextKey = useRef(0);
  const inFlight = useRef<AbortController | null>(null);
  const input = useRef<HTMLTextAreaElement>(null);
  const savedId = conversationId ?? createdId;

  useEffect(() => () => inFlight.current?.abort(), []);

  const update = ({ key, signal }: Asking, change: (turn: Turn) => Partial<Turn>) => {
    if (signal.aborted) return;
    setTurns((list) =>
      list.map((turn) => (turn.key === key ? { ...turn, ...change(turn) } : turn)),
    );
  };

  // 5xx from the gateway: the assistant is unavailable; any other 4xx: a generic failure. A 403
  // is a permission taken away: /me again, which closes the chat if chat.ask is gone.
  const fail = (asking: Asking, status: number) => {
    if (status === 403) void queryClient.invalidateQueries({ queryKey: meKey });
    update(asking, () => ({ status: status >= 500 ? "unavailable" : "error" }));
  };

  // A 429: nothing was sent, so the question goes back to the composer.
  const giveBack = ({ key, question }: Asking) => {
    setTurns((list) => list.filter((turn) => turn.key !== key));
    setDraft(question);
  };

  // The rate limit, not the quota: Send waits for Retry-After (seconds), the quota is unchanged.
  const holdForRateLimit = (asking: Asking, response: Response) => {
    if (asking.signal.aborted) return;
    giveBack(asking);
    const now = Date.now();
    // ponytail: a 429 without Retry-After waits a minute; the core always sends one.
    const until = now + (seconds(response.headers, "Retry-After") ?? 60) * 1000;
    const when = untilReset(until, now);
    rateLimit.hold({ until, when });
    announce(copy.chat.rateLimitedTitle);
    announce(format(copy.chat.rateLimitedBody, { when }));
  };

  // The daily quota: the question goes back, and the notice is said.
  const quotaReached = (asking: Asking, quota: ChatQuota | undefined) => {
    if (asking.signal.aborted) return;
    giveBack(asking);
    if (!quota) return;
    const notice = quotaNoticeText(quota);
    announce(notice.title);
    announce(notice.body);
  };

  /** The quota a post's headers leave (a 429 here is the daily quota: none left). */
  const recordQuota = (response: Response) => {
    const before = queryClient.getQueryData<ChatQuota>(quotaKey);
    const after = quotaAfter(response, before);
    if (after && after !== before) queryClient.setQueryData(quotaKey, after);
    return after;
  };

  /**
   * The first question creates the conversation (D4), at the top of the list right away, titled
   * by this question. Resolves its id, or undefined once a refusal ended the turn.
   */
  const createConversation = async (asking: Asking) => {
    const created = await api.request(establishmentConversationsPath(establishment.id), {
      method: "POST",
      signal: asking.signal,
    });
    if (!created.ok) {
      if (await rateLimited(created)) holdForRateLimit(asking, created);
      else fail(asking, created.status);
      return undefined;
    }
    const summary = (await created.json()) as ConversationSummary;
    setCreatedId(summary.id);
    queryClient.setQueryData<ConversationSummary[]>(conversationsKey, (list = []) => [
      { ...summary, title: asking.question },
      ...list.filter((item) => item.id !== summary.id),
    ]);
    onCreated(summary.id);
    return summary.id;
  };

  const postQuestion = (asking: Asking, id: string) =>
    api.request(conversationMessagesPath(establishment.id, id), {
      method: "POST",
      json: { text: asking.question },
      accept: "text/event-stream",
      signal: asking.signal,
    });

  /**
   * What ends the turn before an answer streams: the rate limit, the daily quota, a 404 or another
   * failure. The quota headers are recorded first, except on the rate limit. Resolves whether the
   * post was refused.
   */
  const handleRefusedPost = async (asking: Asking, response: Response) => {
    if (await rateLimited(response)) {
      holdForRateLimit(asking, response);
      return true;
    }
    const quota = recordQuota(response);
    if (response.ok) return false;
    if (response.status === 429) quotaReached(asking, quota);
    else if (response.status === 404) {
      if (!asking.signal.aborted) setNotFound(true);
    } else fail(asking, response.status);
    return true;
  };

  /** The answer's claims into the turn as they arrive, each said; then how it ended. */
  const streamIntoTurn = async (asking: Asking, response: Response) => {
    update(asking, () => ({ status: "streaming" }));
    let shown = 0;
    const end = await streamAnswer(response, (claim, language) => {
      if (asking.signal.aborted) return;
      shown += 1;
      update(asking, (turn) => ({
        claims: [...turn.claims, claim],
        language: turn.language ?? language,
      }));
      // A Portuguese claim in the English interface is read with a Portuguese voice.
      announce(claim.text, language);
    });
    if (asking.signal.aborted) return;
    update(asking, () => ({ status: end }));
    if (end === answerEvents.done && shown > 0) announce(copy.chat.answerComplete);
    else if (end === answerEvents.done || end === answerEvents.refusal) {
      announce(format(copy.chat.refusal, { regulation }));
      announce(copy.chat.refusalHint);
    }
  };

  async function ask(key: string, question: string) {
    const abort = new AbortController();
    inFlight.current = abort;
    const asking = { key, question, signal: abort.signal };
    announce(format(copy.chat.checking, { regulation }));
    try {
      const id = savedId ?? (await createConversation(asking));
      if (id === undefined) return;
      const response = await postQuestion(asking, id);
      if (!(await handleRefusedPost(asking, response))) await streamIntoTurn(asking, response);
    } catch {
      // The request failed (a 401 after renewal also lands here, as the session-expired flow
      // takes over the screen).
      update(asking, () => ({ status: "network" }));
    } finally {
      if (inFlight.current === abort) inFlight.current = null;
      void queryClient.invalidateQueries({ queryKey: conversationsKey });
    }
  }

  function submit() {
    const question = draft.trim();
    const key = `asked-${nextKey.current++}`;
    onAsk();
    setTurns((list) => [
      ...list,
      { key, question, status: "pending", claims: [], live: true, attempt: 0 },
    ]);
    setDraft("");
    void ask(key, question);
  }

  function retry(turn: Turn) {
    setTurns((list) =>
      list.map((item) =>
        item.key === turn.key
          ? { ...item, status: "pending", claims: [], attempt: item.attempt + 1 }
          : item,
      ),
    );
    void ask(turn.key, turn.question);
    // The button goes away with the failure; focus goes where Send leaves it.
    input.current?.focus();
  }

  return {
    turns,
    createdId,
    savedId,
    notFound,
    busy: turns.some(answering),
    draft,
    setDraft,
    input,
    submit,
    retry,
    limitedWhen: rateLimit.limitedWhen,
  };
}

/**
 * Which view the pane shows, from the stored conversation and the turns asked here. The stored one
 * is fetched unless this view created it (it has nothing stored that is not already shown). One
 * of another of the user's establishments selects that establishment; anyone else's is not found.
 */
export function useConversationView({
  establishment,
  conversationId,
  knownTitle,
  createdId,
  notFound,
  asked,
  onEstablishment,
}: {
  establishment: Establishment;
  conversationId: string | undefined;
  /** The title the list already has, shown while the conversation loads. */
  knownTitle: string | null | undefined;
  createdId: string | undefined;
  /** A send answered 404. */
  notFound: boolean;
  /** The turns asked in this view. */
  asked: Turn[];
  onEstablishment: (establishment: Establishment) => void;
}) {
  const establishments = useEstablishments();
  const load = conversationId !== undefined && conversationId !== createdId;
  const conversation = useConversation(load ? conversationId : undefined);
  const loadStatus = conversation.error instanceof ApiError ? conversation.error.status : 0;
  // A 404, or a 422 for an id that is not one: the not-found view, which has no Try again.
  const loadNotFound = loadStatus === 404 || loadStatus === 422;
  const loadRetry = useQueryRetry(
    conversation,
    TITLE_ID,
    queryFailed(conversation) && !loadNotFound,
  );
  const loadFailed = load && loadRetry.failed;
  const stored = conversation.data;
  const owner =
    stored && stored.establishmentId !== establishment.id
      ? establishments.data?.find((item) => item.id === stored.establishmentId)
      : undefined;

  let view: ConversationView;
  if (notFound) view = "not-found";
  else if (!load) view = asked.length > 0 ? "ready" : "new";
  else if (loadFailed) view = loadNotFound ? "not-found" : "error";
  else if (!stored) view = "loading";
  // Another of the user's establishments: it gets selected (below). Anyone else's: not found.
  else if (stored.establishmentId !== establishment.id) view = owner ? "loading" : "not-found";
  else view = "ready";

  useEffect(() => {
    if (owner) onEstablishment(owner);
  }, [owner, onEstablishment]);

  const turns = [...(view === "ready" && stored ? toTurns(stored.messages) : []), ...asked];
  const title =
    view === "new"
      ? copy.chat.newTitle
      : (stored?.title ??
        knownTitle ??
        asked[0]?.question ??
        (view === "loading" ? undefined : copy.chat.newTitle));

  return {
    view,
    turns,
    lastTurn: turns.at(-1),
    title,
    retryingLoad: load && loadRetry.retrying,
    reload: loadRetry.retry,
  };
}

/**
 * A 404 (on send, or on load while typing) replaces the composer that had focus: the way out
 * (`startNew`) takes it, so focus does not fall to the page. The composer's wrapper reports its
 * focus with `onComposerFocus` and `onComposerBlur`.
 */
export function useNotFoundFocus(view: ConversationView) {
  const composerFocused = useRef(false);
  const startNew = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (view !== "not-found" || !composerFocused.current) return;
    if (focusIsLost()) startNew.current?.focus();
  }, [view]);

  const onComposerFocus = () => {
    composerFocused.current = true;
  };

  // Only a move to another element counts: removing a focused element may blur it too.
  const onComposerBlur = (event: FocusEvent) => {
    if (event.relatedTarget) composerFocused.current = false;
  };

  return { startNew, onComposerFocus, onComposerBlur };
}

/**
 * Deletes the saved conversation and drops it from the list; resolves the refusal to show when it
 * could not be deleted, undefined once it is gone. A 404 means it is already gone, which is what
 * the user asked for. Sent from the confirmation dialog, which calls `closed()` when it closes: a
 * 403 refetches /me only then.
 */
export function useDeleteConversation(savedId: string | undefined, onDeleted: () => void) {
  const { send, closed } = useSend({ inside: true });
  const queryClient = useQueryClient();

  async function deleteConversation(): Promise<string | undefined> {
    if (savedId === undefined) return copy.chat.deleteError;
    const result = await send(conversationPath(savedId), { method: "DELETE" });
    if (!result.ok && result.status !== 404) {
      return refusalText(result, copy.chat.deleteError);
    }
    queryClient.setQueryData<ConversationSummary[]>(conversationsKey, (list) =>
      list?.filter((item) => item.id !== savedId),
    );
    onDeleted();
    return undefined;
  }

  return { deleteConversation, closed };
}
