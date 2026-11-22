import { CircleAlert, History, SquarePen, Trash2 } from "lucide-react";
import { AlertDialog } from "radix-ui";
import { Fragment, useRef, useState } from "react";
import { useChatQuota, type Citation } from "../api/chat";
import type { Establishment } from "../api/queries";
import { useSession } from "../auth/session";
import { copy, format } from "../copy";
import {
  Avatar,
  Button,
  dialogPanel,
  iconButton,
  Message,
  Skeleton,
  Spinner,
  TypeTile,
} from "../ui/primitives";
import { Answer } from "./Answer";
import { Composer } from "./Composer";
import {
  SHOW_CONVERSATIONS_ID,
  TITLE_ID,
  useAnswerStream,
  useConversationView,
  useDeleteConversation,
  useNotFoundFocus,
  useStickToBottom,
} from "./conversation";

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
  /** Says it in the polite live region; `lang` when it is not in the interface language. */
  announce: (text: string, lang?: string) => void;
}

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
  const { profile } = useSession();
  const quota = useChatQuota().data;
  const place = copy.jurisdictions[establishment.jurisdiction];
  const regulation = place?.regulation ?? establishment.jurisdiction;
  const { scroller, content, onScroll, follow } = useStickToBottom();
  const asked = useAnswerStream({
    establishment,
    conversationId,
    regulation,
    announce,
    onCreated,
    onAsk: follow,
  });
  const { view, turns, lastTurn, title, retryingLoad, reload } = useConversationView({
    establishment,
    conversationId,
    knownTitle,
    createdId: asked.createdId,
    notFound: asked.notFound,
    asked: asked.turns,
    onEstablishment,
  });
  const { startNew, onComposerFocus, onComposerBlur } = useNotFoundFocus(view);
  const { deleteConversation, closed } = useDeleteConversation(asked.savedId, onDeleted);

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
        {asked.savedId !== undefined && view === "ready" && (
          <DeleteConversation
            title={title ?? copy.conversations.untitled}
            busy={asked.busy}
            onDelete={deleteConversation}
            onClosed={closed}
          />
        )}
      </header>

      <div ref={scroller} onScroll={onScroll} className="min-h-0 flex-1 overflow-y-auto p-5">
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

        {/* Said once: by the heading while the title is unknown, else here. */}
        {view === "loading" && <LoadingTurns label={title !== undefined} />}

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
              onClick={retryingLoad ? undefined : reload}
            >
              {retryingLoad && <Spinner />}
              {copy.chat.tryAgain}
            </Button>
          </div>
        )}

        {view === "not-found" && (
          <div role="alert" className="mx-auto w-full max-w-180">
            <Message
              className="items-start"
              icon={<CircleAlert aria-hidden="true" className="size-5 text-text-secondary" />}
              title={copy.chat.notFoundTitle}
              body={copy.chat.notFoundBody}
            />
            <Button ref={startNew} variant="secondary" className="mt-4" onClick={onNew}>
              {copy.chat.startNew}
            </Button>
          </div>
        )}

        {view === "ready" && (
          <ol
            ref={content}
            aria-label={copy.chat.messages}
            className="mx-auto flex w-full max-w-180 flex-col gap-4"
          >
            {turns.map((turn) => (
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
                    onRetry={turn.live && turn === lastTurn ? () => asked.retry(turn) : undefined}
                    retryCosts={quota?.limit != null}
                  />
                )}
              </Fragment>
            ))}
          </ol>
        )}
      </div>

      {view !== "not-found" && (
        <div onFocus={onComposerFocus} onBlur={onComposerBlur}>
          <Composer
            value={asked.draft}
            onChange={asked.setDraft}
            onSubmit={asked.submit}
            regulation={regulation}
            ready={(view === "new" || view === "ready") && !asked.busy}
            quota={quota}
            limitedWhen={asked.limitedWhen}
            inputRef={asked.input}
          />
        </div>
      )}
    </section>
  );
}

/** Two skeleton turns: a question bar, then an answer with three lines and a chip. */
function LoadingTurns({ label }: { label: boolean }) {
  return (
    <>
      {label && <span className="sr-only">{copy.chat.loading}</span>}
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
  onClosed,
}: {
  title: string;
  /** An answer is in progress. */
  busy: boolean;
  /** Resolves the refusal to show when the conversation could not be deleted. */
  onDelete: () => Promise<string | undefined>;
  /** The dialog closed: a refusal it showed may now refetch /me. */
  onClosed: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const trigger = useRef<HTMLButtonElement>(null);

  const confirm = async () => {
    setDeleting(true);
    setRefusal(undefined);
    const refused = await onDelete();
    if (refused === undefined) return;
    setDeleting(false);
    setRefusal(refused);
  };

  return (
    <AlertDialog.Root
      open={open}
      onOpenChange={(next) => {
        if (deleting) return;
        setOpen(next);
        setRefusal(undefined);
        if (!next) onClosed();
      }}
    >
      <AlertDialog.Trigger
        ref={trigger}
        aria-label={copy.chat.deleteConversation}
        aria-disabled={busy}
        onClick={(event) => {
          if (busy) event.preventDefault();
        }}
        className={`${iconButton} -mr-2 aria-disabled:opacity-50 aria-disabled:hover:bg-transparent`}
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
          className={dialogPanel}
        >
          <AlertDialog.Title className="text-title font-semibold">
            {copy.chat.deleteTitle}
          </AlertDialog.Title>
          <AlertDialog.Description className="mt-2 text-body text-text-secondary">
            {format(copy.chat.deleteBody, { title })}
          </AlertDialog.Description>
          {refusal && (
            <p role="alert" className="mt-3 text-body">
              {refusal}
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
              {deleting && <Spinner surface="mint" />}
              {copy.chat.deleteConfirm}
            </Button>
          </div>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  );
}
