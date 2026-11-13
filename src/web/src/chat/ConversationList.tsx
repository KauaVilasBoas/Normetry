import { CircleAlert, MessagesSquare, Plus, X } from "lucide-react";
import { Dialog } from "radix-ui";
import { useRef } from "react";
import { Link } from "react-router";
import { useConversations, type Establishment } from "../api/queries";
import { copy, format, locale } from "../copy";
import { Button, iconButton, Message, Skeleton, Spinner } from "../ui/primitives";

/** The time today, "Nov 10" this year, "Nov 10, 2025" before. */
function shortDate(iso: string, now: Date): string {
  const date = new Date(iso);
  const options: Intl.DateTimeFormatOptions =
    date.toDateString() === now.toDateString()
      ? { hour: "numeric", minute: "2-digit" }
      : date.getFullYear() === now.getFullYear()
        ? { month: "short", day: "numeric" }
        : { month: "short", day: "numeric", year: "numeric" };
  return new Intl.DateTimeFormat(locale, options).format(date);
}

/** The selected establishment's conversations, latest first. A card, or a sheet's content. */
export function ConversationList({
  establishment,
  activeId,
  onNew,
  onOpen,
  inSheet,
}: {
  establishment: Establishment;
  activeId: string | undefined;
  onNew: () => void;
  /** A row was chosen (its link navigates). */
  onOpen: () => void;
  inSheet: boolean;
}) {
  const conversations = useConversations();
  const title = useRef<HTMLHeadingElement>(null);
  // Try again sends a failed query back to pending: keep the error (and its focused button) until
  // that request settles.
  const failed =
    conversations.isError || (conversations.isPending && conversations.errorUpdateCount > 0);
  const retrying = failed && conversations.isFetching;
  const rows = (conversations.data ?? [])
    .filter((conversation) => conversation.establishmentId === establishment.id)
    .sort((a, b) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt));
  const now = new Date();

  const heading = (
    <h2 ref={title} tabIndex={-1} className="text-title font-semibold">
      {copy.conversations.title}
    </h2>
  );

  // Try again: once the list loads the button goes away, so focus moves to the heading.
  const reload = async () => {
    if ((await conversations.refetch()).isSuccess) title.current?.focus();
  };

  return (
    <div
      className={`flex min-h-0 w-full flex-col ${inSheet ? "flex-1" : "rounded-card border border-border bg-card"}`}
    >
      <div className="p-4">
        {inSheet ? (
          <div className="flex items-center justify-between gap-2">
            <Dialog.Title asChild>{heading}</Dialog.Title>
            <Dialog.Close
              aria-label={copy.conversations.close}
              className={`${iconButton} -my-2 -mr-2`}
            >
              <X aria-hidden="true" className="size-5" />
            </Dialog.Close>
          </div>
        ) : (
          heading
        )}
        <Button variant="secondary" className="mt-3 w-full" onClick={onNew}>
          <Plus aria-hidden="true" className="size-4" />
          {copy.conversations.newConversation}
        </Button>
      </div>
      <nav
        aria-label={copy.conversations.title}
        aria-busy={(!failed && conversations.isPending) || retrying}
        className="min-h-0 flex-1 overflow-y-auto p-2 pt-0"
      >
        {failed ? (
          <div role="alert" className="px-2 pb-2">
            <Message
              icon={<CircleAlert aria-hidden="true" className="size-5 text-text-secondary" />}
              title={copy.conversations.errorTitle}
              body={copy.conversations.errorBody}
            />
            {/* aria-disabled, not disabled, while retrying: focus stays on the button. */}
            <Button
              variant="secondary"
              className="mt-4"
              aria-disabled={retrying}
              onClick={retrying ? undefined : () => void reload()}
            >
              {retrying && <Spinner />}
              {copy.conversations.tryAgain}
            </Button>
          </div>
        ) : conversations.isPending ? (
          <>
            <span className="sr-only">{copy.conversations.loading}</span>
            <ul aria-hidden="true">
              {[0, 1, 2, 3].map((row) => (
                <li key={row} className="flex min-h-12 flex-col justify-center gap-2 px-3">
                  <Skeleton className="h-3 w-3/5" />
                  <Skeleton className="h-2.5 w-3/10" />
                </li>
              ))}
            </ul>
          </>
        ) : rows.length === 0 ? (
          <div className="px-2 pb-2">
            <Message
              icon={<MessagesSquare aria-hidden="true" className="size-5 text-neutral" />}
              title={copy.conversations.emptyTitle}
              body={format(copy.conversations.emptyBody, { establishment: establishment.name })}
            />
          </div>
        ) : (
          <ul>
            {rows.map((conversation) => {
              const active = conversation.id === activeId;
              return (
                <li key={conversation.id}>
                  <Link
                    to={`/conversations/${conversation.id}`}
                    onClick={onOpen}
                    aria-current={active ? "page" : undefined}
                    className={`flex min-h-12 flex-col justify-center rounded-control px-3 py-1.5 focus-visible:-outline-offset-2 ${
                      active ? "bg-teal" : "hover:bg-elevated"
                    }`}
                  >
                    <span className="truncate text-body font-medium">
                      {conversation.title ?? copy.conversations.untitled}
                    </span>
                    {/* On --teal the date stays white: secondary text there is only 3.6:1. */}
                    <time
                      dateTime={conversation.updatedAt}
                      className={`text-caption tabular-nums ${active ? "text-text-primary" : "text-text-secondary"}`}
                    >
                      {shortDate(conversation.updatedAt, now)}
                    </time>
                  </Link>
                </li>
              );
            })}
          </ul>
        )}
      </nav>
    </div>
  );
}
