import { useQueryClient } from "@tanstack/react-query";
import { CircleAlert, History, SquarePen, Trash2 } from "lucide-react";
import { AlertDialog } from "radix-ui";
import { Fragment, useEffect, useRef, useState } from "react";
import { ApiError } from "../api/client";
import {
  conversationsKey,
  quotaKey,
  useChatQuota,
  useConversation,
  useEstablishments,
  type ChatQuota,
  type Citation,
  type ConversationSummary,
  type Establishment,
} from "../api/queries";
import { useSession } from "../auth/session";
import { copy, format } from "../copy";
import { Avatar, Button, iconButton, Message, Skeleton, Spinner, TypeTile } from "../ui/primitives";
import { Answer } from "./Answer";
import { quotaAfter, quotaNoticeText, streamAnswer, toTurns, type Turn } from "./answers";
import { Composer } from "./Composer";

export const TITLE_ID = "conversation-title";
export const SHOW_CONVERSATIONS_ID = "show-conversations";

/** Follow new content only while the reader is this close to the bottom. */
const STICK_PX = 80;

interface PaneProps {
  establishment: Establishment;
  /** From the URL; undefined for a new conversation. */
  conversationId: string | undefined;
  /** The title the list already has, shown while the conversation loads. */
  knownTitle: string | null | undefined;
  /** Below 768px: the header also shows the conversations and New conversation. */
  compact: boolean;
  openCitationId: string | undefined;
  onOpenSource: (citation: Citation) => void;
  /** The conversation belongs to another of the user's establishments: select it. */
  onEstablishment: (establishment: Establishment) => void;
  /** The first question created the conversation (D4): the URL takes its id. */
  onCreated: (id: string) => void;
  onDeleted: () => void;
  onNew: () => void;
  onShowConversations: () => void;
  announce: (text: string) => void;
}

const path = (id: string) => encodeURIComponent(id);

/**
 * One conversation: stored turns, then the ones asked here. Mounted per conversation, so leaving
 * it aborts the answer in progress; the server stores what was sent, and reopening shows it.
 */
export function ConversationPane({
  establishment,
  conversationId,
  knownTitle,
  compact,
  openCitationId,
  onOpenSource,
  onEstablishment,
  onCreated,
  onDeleted,
  onNew,
  onShowConversations,
  announce,
}: PaneProps) {
  const { api, profile } = useSession();
  const queryClient = useQueryClient();
  const establishments = useEstablishments();
  const quota = useChatQuota().data;
  const place = copy.jurisdictions[establishment.jurisdiction];
  const regulation = place?.regulation ?? establishment.jurisdiction;

  const [createdId, setCreatedId] = useState<string>();
  const [turns, setTurns] = useState<Turn[]>([]);
  const [draft, setDraft] = useState("");
  const [notFound, setNotFound] = useState(false);
  const nextKey = useRef(0);
  const inFlight = useRef<AbortController | null>(null);
  const input = useRef<HTMLTextAreaElement>(null);
  const scroller = useRef<HTMLDivElement>(null);
  const list = useRef<HTMLOListElement>(null);
  const stick = useRef(true);

  useEffect(() => () => inFlight.current?.abort(), []);

  // A conversation this view created has nothing stored that it does not already show.
  const savedId = conversationId ?? createdId;
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

  let view: "new" | "loading" | "error" | "not-found" | "ready";
  if (notFound) view = "not-found";
  else if (!load) view = turns.length > 0 ? "ready" : "new";
  else if (loadFailed) view = loadStatus === 404 || loadStatus === 422 ? "not-found" : "error";
  else if (!stored) view = "loading";
  // Another of the user's establishments: it gets selected (below). Anyone else's: not found.
  else if (stored.establishmentId !== establishment.id) view = owner ? "loading" : "not-found";
  else view = "ready";

  useEffect(() => {
    if (owner) onEstablishment(owner);
  }, [owner, onEstablishment]);

  const allTurns = [...(view === "ready" && stored ? toTurns(stored.messages) : []), ...turns];
  const lastTurn = allTurns.at(-1);
  const busy = turns.some((turn) => turn.status === "pending" || turn.status === "streaming");
  const title =
    view === "new"
      ? copy.chat.newTitle
      : (stored?.title ??
        knownTitle ??
        turns[0]?.question ??
        (view === "loading" ? undefined : copy.chat.newTitle));

  // Keeps the latest content in view while the reader is at the bottom; leaves them if they
  // scrolled up. The list appears with the ready view.
  useEffect(() => {
    const area = scroller.current;
    const content = list.current;
    if (!area || !content) return;
    const observer = new ResizeObserver(() => {
      if (stick.current) area.scrollTop = area.scrollHeight;
    });
    observer.observe(content);
    return () => observer.disconnect();
  }, [view]);

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
    // 5xx from the gateway: the assistant is unavailable; any other 4xx: a generic failure.
    const fail = (status: number) =>
      update(() => ({ status: status >= 500 ? "unavailable" : "error" }));
    announce(format(copy.chat.checking, { regulation }));
    try {
      let id = savedId;
      if (id === undefined) {
        const created = await api.request(
          `/ai/establishments/${path(establishment.id)}/conversations`,
          { method: "POST", signal },
        );
        if (!created.ok) return fail(created.status);
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
      const before = queryClient.getQueryData<ChatQuota>(quotaKey);
      const after = quotaAfter(response, before);
      if (after && after !== before) queryClient.setQueryData(quotaKey, after);

      if (response.status === 429) {
        // Nothing was sent: the question goes back to the composer.
        if (signal.aborted) return;
        setTurns((list) => list.filter((turn) => turn.key !== key));
        setDraft(question);
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
      const end = await streamAnswer(response, (claim) => {
        if (signal.aborted) return;
        shown += 1;
        update((turn) => ({ claims: [...turn.claims, claim] }));
        announce(claim.text);
      });
      if (signal.aborted) return;
      update(() => ({ status: end }));
      if (end === "done" && shown > 0) announce(copy.chat.answerComplete);
      else if (end === "done" || end === "refusal") {
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
    stick.current = true;
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

  async function deleteConversation(): Promise<boolean> {
    if (savedId === undefined) return false;
    try {
      const response = await api.request(`/ai/conversations/${path(savedId)}`, {
        method: "DELETE",
      });
      // 404: it is already gone, which is what the user asked for.
      if (!response.ok && response.status !== 404) return false;
    } catch {
      return false;
    }
    queryClient.setQueryData<ConversationSummary[]>(conversationsKey, (list) =>
      list?.filter((item) => item.id !== savedId),
    );
    onDeleted();
    return true;
  }

  const userName = profile.name ?? profile.email ?? "";
  return (
    <section
      aria-labelledby={TITLE_ID}
      aria-busy={view === "loading" || retryingLoad}
      className="flex min-h-0 min-w-0 flex-1 flex-col rounded-card border border-border bg-card motion-safe:animate-fade-in"
    >
      <header className="flex items-center gap-2 border-b border-border px-5 py-4">
        {compact && (
          <button
            type="button"
            id={SHOW_CONVERSATIONS_ID}
            aria-label={copy.conversations.show}
            onClick={onShowConversations}
            className={`${iconButton} -ml-2`}
          >
            <History aria-hidden="true" className="size-5" />
          </button>
        )}
        <div className="min-w-0 flex-1">
          <h2 id={TITLE_ID} tabIndex={-1} className="truncate text-title font-semibold">
            {title ?? (
              <>
                <Skeleton className="my-0.5 h-4 w-48 max-w-full" />
                <span className="sr-only">{copy.chat.loading}</span>
              </>
            )}
          </h2>
          <p className="truncate text-small text-text-secondary">
            {format(copy.chat.meta, { establishment: establishment.name, regulation })}
          </p>
        </div>
        {compact && (
          <button
            type="button"
            aria-label={copy.conversations.newConversation}
            onClick={onNew}
            className={iconButton}
          >
            <SquarePen aria-hidden="true" className="size-5" />
          </button>
        )}
        {savedId !== undefined && view === "ready" && (
          <DeleteConversation
            title={title ?? copy.conversations.untitled}
            busy={busy}
            onDelete={deleteConversation}
          />
        )}
      </header>

      <div
        ref={scroller}
        onScroll={(event) => {
          const area = event.currentTarget;
          stick.current = area.scrollHeight - area.scrollTop - area.clientHeight <= STICK_PX;
        }}
        className="min-h-0 flex-1 overflow-y-auto p-5"
      >
        {view === "new" && (
          <div className="flex min-h-full flex-col items-center justify-center gap-2 text-center">
            <TypeTile type={establishment.type} />
            <h3 className="mt-1 text-title font-semibold">
              {format(copy.chat.emptyTitle, { regulation })}
            </h3>
            {place && <p className="max-w-90 text-body text-text-secondary">{place.askLanguage}</p>}
            <p className="max-w-90 text-body text-text-secondary">{copy.chat.emptyBody}</p>
          </div>
        )}

        {view === "loading" && <LoadingTurns />}

        {view === "error" && (
          <div role="alert" className="mx-auto w-full max-w-180">
            <Message
              className="items-start"
              icon={<CircleAlert aria-hidden="true" className="size-5 text-text-secondary" />}
              title={copy.chat.loadErrorTitle}
              body={copy.chat.loadErrorBody}
            />
            <Button
              variant="secondary"
              className="mt-4"
              aria-disabled={retryingLoad}
              onClick={retryingLoad ? undefined : () => void conversation.refetch()}
            >
              {retryingLoad && <Spinner />}
              {copy.chat.tryAgain}
            </Button>
          </div>
        )}

        {view === "not-found" && (
          <div className="mx-auto w-full max-w-180">
            <Message
              className="items-start"
              icon={<CircleAlert aria-hidden="true" className="size-5 text-text-secondary" />}
              title={copy.chat.notFoundTitle}
              body={copy.chat.notFoundBody}
            />
            <Button variant="secondary" className="mt-4" onClick={onNew}>
              {copy.chat.startNew}
            </Button>
          </div>
        )}

        {view === "ready" && (
          <ol
            ref={list}
            aria-label={copy.chat.messages}
            className="mx-auto flex w-full max-w-180 flex-col gap-4"
          >
            {allTurns.map((turn) => (
              <Fragment key={turn.key}>
                <li className="flex justify-end gap-3">
                  <p className="max-w-[85%] rounded-control bg-bubble-user px-3.5 py-2.5 text-body whitespace-pre-wrap wrap-break-word">
                    <span className="sr-only">{copy.chat.you} </span>
                    {turn.question}
                  </p>
                  <Avatar name={userName} />
                </li>
                {turn.status && (
                  <Answer
                    turn={turn}
                    regulation={regulation}
                    openCitationId={openCitationId}
                    onOpenSource={onOpenSource}
                    onRetry={turn.live && turn === lastTurn ? () => retry(turn) : undefined}
                    retryCosts={quota?.limit != null}
                  />
                )}
              </Fragment>
            ))}
          </ol>
        )}
      </div>

      {view !== "not-found" && (
        <Composer
          value={draft}
          onChange={setDraft}
          onSubmit={submit}
          regulation={regulation}
          ready={(view === "new" || view === "ready") && !busy}
          quota={quota}
          inputRef={input}
        />
      )}
    </section>
  );
}

/** Two skeleton turns: a question bar, then an answer with three lines and a chip. */
function LoadingTurns() {
  return (
    <>
      <span className="sr-only">{copy.chat.loading}</span>
      <div aria-hidden="true" className="mx-auto flex w-full max-w-180 flex-col gap-4">
        <div className="flex justify-end">
          <Skeleton className="h-9 w-1/2" />
        </div>
        <div className="ml-11 flex flex-col gap-2 rounded-card border border-border p-4">
          <Skeleton className="h-3 w-full" />
          <Skeleton className="h-3 w-11/12" />
          <Skeleton className="h-3 w-2/3" />
          <Skeleton className="mt-1 h-7 w-36" />
        </div>
      </div>
    </>
  );
}

/** Header action and its confirmation; no red button (DESIGN.md). */
function DeleteConversation({
  title,
  busy,
  onDelete,
}: {
  title: string;
  /** An answer is in progress. */
  busy: boolean;
  /** Resolves false when the conversation could not be deleted. */
  onDelete: () => Promise<boolean>;
}) {
  const [open, setOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [failed, setFailed] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);

  const confirm = async () => {
    setDeleting(true);
    setFailed(false);
    if (await onDelete()) return;
    setDeleting(false);
    setFailed(true);
  };

  return (
    <AlertDialog.Root
      open={open}
      onOpenChange={(next) => {
        if (deleting) return;
        setOpen(next);
        setFailed(false);
      }}
    >
      <AlertDialog.Trigger
        ref={trigger}
        aria-label={copy.chat.deleteConversation}
        aria-disabled={busy}
        onClick={(event) => {
          if (busy) event.preventDefault();
        }}
        className={`${iconButton} -mr-2`}
      >
        <Trash2 aria-hidden="true" className="size-5" />
      </AlertDialog.Trigger>
      <AlertDialog.Portal>
        <AlertDialog.Overlay className="fixed inset-0 z-40 bg-scrim" />
        <AlertDialog.Content
          // Back to the trigger after Cancel; after a delete the next view takes focus.
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            if (trigger.current?.isConnected) trigger.current.focus();
          }}
          className="fixed top-1/2 left-1/2 z-40 w-[calc(100%-2rem)] max-w-100 -translate-x-1/2 -translate-y-1/2 rounded-card border border-border bg-elevated p-5 motion-safe:animate-fade-in"
        >
          <AlertDialog.Title className="text-title font-semibold">
            {copy.chat.deleteTitle}
          </AlertDialog.Title>
          <AlertDialog.Description className="mt-2 text-body text-text-secondary">
            {format(copy.chat.deleteBody, { title })}
          </AlertDialog.Description>
          {failed && (
            <p role="alert" className="mt-3 text-body">
              {copy.chat.deleteError}
            </p>
          )}
          <div className="mt-5 flex justify-end gap-2">
            <AlertDialog.Cancel asChild>
              <Button variant="secondary" onElevated>
                {copy.chat.deleteCancel}
              </Button>
            </AlertDialog.Cancel>
            <Button
              variant="primary"
              aria-disabled={deleting}
              onClick={deleting ? undefined : () => void confirm()}
            >
              {deleting && <Spinner />}
              {copy.chat.deleteConfirm}
            </Button>
          </div>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  );
}
