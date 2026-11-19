import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState, type FocusEvent, type UIEvent } from "react";
import { ApiError, seconds } from "../api/client";
import { answerEvents } from "../api/contract";
import {
  conversationsKey,
  meKey,
  quotaKey,
  useConversation,
  useEstablishments,
  type ChatQuota,
  type ConversationSummary,
  type Establishment,
} from "../api/queries";
import { useSession } from "../auth/session";
import { copy, format } from "../copy";
import {
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

/** Follow new content only while the reader is this close to the bottom. */
const STICK_PX = 80;

const path = (id: string) => encodeURIComponent(id);

export type ConversationView = "new" | "loading" | "error" | "not-found" | "ready";

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

  async function ask(key: string, question: string) {
    const abort = new AbortController();
    inFlight.current = abort;
    const { signal } = abort;
    const update = (change: (turn: Turn) => Partial<Turn>) => {
      if (signal.aborted) return;
      setTurns((list) =>
        list.map((turn) => (turn.key === key ? { ...turn, ...change(turn) } : turn)),
      );
    };
    // 5xx from the gateway: the assistant is unavailable; any other 4xx: a generic failure. A 403
    // is a permission taken away: /me again, which closes the chat if chat.ask is gone.
    const fail = (status: number) => {
      if (status === 403) void queryClient.invalidateQueries({ queryKey: meKey });
      update(() => ({ status: status >= 500 ? "unavailable" : "error" }));
    };
    // A 429: nothing was sent, so the question goes back to the composer.
    const giveBack = () => {
      setTurns((list) => list.filter((turn) => turn.key !== key));
      setDraft(question);
    };
    // The rate limit, not the quota: Send waits for Retry-After (seconds), the quota is unchanged.
    const limited = (response: Response) => {
      if (signal.aborted) return;
      giveBack();
      const now = Date.now();
      // ponytail: a 429 without Retry-After waits a minute; the core always sends one.
      const until = now + (seconds(response.headers, "Retry-After") ?? 60) * 1000;
      const when = untilReset(until, now);
      rateLimit.hold({ until, when });
      announce(copy.chat.rateLimitedTitle);
      announce(format(copy.chat.rateLimitedBody, { when }));
    };
    announce(format(copy.chat.checking, { regulation }));
    try {
      let id = savedId;
      if (id === undefined) {
        const created = await api.request(
          `/ai/establishments/${path(establishment.id)}/conversations`,
          { method: "POST", signal },
        );
        if (!created.ok)
          return (await rateLimited(created)) ? limited(created) : fail(created.status);
        const summary = (await created.json()) as ConversationSummary;
        id = summary.id;
        setCreatedId(id);
        // At the top of the list right away, titled by this question.
        queryClient.setQueryData<ConversationSummary[]>(conversationsKey, (list = []) => [
          { ...summary, title: question },
          ...list.filter((item) => item.id !== summary.id),
        ]);
        onCreated(id);
      }

      const response = await api.request(
        `/ai/establishments/${path(establishment.id)}/conversations/${path(id)}/messages`,
        { method: "POST", json: { text: question }, accept: "text/event-stream", signal },
      );
      if (await rateLimited(response)) return limited(response);
      const before = queryClient.getQueryData<ChatQuota>(quotaKey);
      const after = quotaAfter(response, before);
      if (after && after !== before) queryClient.setQueryData(quotaKey, after);

      if (response.status === 429) {
        // The daily quota.
        if (signal.aborted) return;
        giveBack();
        if (after) {
          const notice = quotaNoticeText(after);
          announce(notice.title);
          announce(notice.body);
        }
        return;
      }
      if (response.status === 404) {
        if (!signal.aborted) setNotFound(true);
        return;
      }
      if (!response.ok) return fail(response.status);

      update(() => ({ status: "streaming" }));
      let shown = 0;
      const end = await streamAnswer(response, (claim, language) => {
        if (signal.aborted) return;
        shown += 1;
        update((turn) => ({
          claims: [...turn.claims, claim],
          language: turn.language ?? language,
        }));
        // A Portuguese claim in the English interface is read with a Portuguese voice.
        announce(claim.text, language);
      });
      if (signal.aborted) return;
      update(() => ({ status: end }));
      if (end === answerEvents.done && shown > 0) announce(copy.chat.answerComplete);
      else if (end === answerEvents.done || end === answerEvents.refusal) {
        announce(format(copy.chat.refusal, { regulation }));
        announce(copy.chat.refusalHint);
      }
    } catch {
      // The request failed (a 401 after renewal also lands here, as the session-expired flow
      // takes over the screen).
      update(() => ({ status: "network" }));
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
    busy: turns.some((turn) => turn.status === "pending" || turn.status === "streaming"),
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
  const loadFailed =
    load && (conversation.isError || (conversation.isPending && conversation.errorUpdateCount > 0));
  const retryingLoad = loadFailed && conversation.isFetching;
  const loadStatus = conversation.error instanceof ApiError ? conversation.error.status : 0;
  const stored = conversation.data;
  const owner =
    stored && stored.establishmentId !== establishment.id
      ? establishments.data?.find((item) => item.id === stored.establishmentId)
      : undefined;

  let view: ConversationView;
  if (notFound) view = "not-found";
  else if (!load) view = asked.length > 0 ? "ready" : "new";
  else if (loadFailed) view = loadStatus === 404 || loadStatus === 422 ? "not-found" : "error";
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

  // Try again after a failed load: once it works the button goes away, so focus moves to the heading.
  async function reload() {
    if ((await conversation.refetch()).isSuccess) document.getElementById(TITLE_ID)?.focus();
  }

  return { view, turns, lastTurn: turns.at(-1), title, retryingLoad, reload };
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
    if (document.activeElement === null || document.activeElement === document.body)
      startNew.current?.focus();
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
 * Deletes the saved conversation and drops it from the list; resolves false when it could not be
 * deleted. A 404 means it is already gone, which is what the user asked for.
 */
export function useDeleteConversation(savedId: string | undefined, onDeleted: () => void) {
  const { api } = useSession();
  const queryClient = useQueryClient();

  return async function deleteConversation(): Promise<boolean> {
    if (savedId === undefined) return false;
    try {
      const response = await api.request(`/ai/conversations/${path(savedId)}`, {
        method: "DELETE",
      });
      if (!response.ok && response.status !== 404) {
        if (response.status === 403) void queryClient.invalidateQueries({ queryKey: meKey });
        return false;
      }
    } catch {
      return false;
    }
    queryClient.setQueryData<ConversationSummary[]>(conversationsKey, (list) =>
      list?.filter((item) => item.id !== savedId),
    );
    onDeleted();
    return true;
  };
}
