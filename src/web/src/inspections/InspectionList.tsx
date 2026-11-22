import { useQueryClient } from "@tanstack/react-query";
import { CircleAlert, ClipboardCheck, Clock, FileText, Play } from "lucide-react";
import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { permissions } from "../api/contract";
import { inspectionsKey, useInspections, type InspectionSummary } from "../api/inspections";
import type { Live } from "../api/live";
import { can, queryFailed, type Establishment, type Me } from "../api/queries";
import { useSend } from "../api/send";
import { untilReset } from "../chat/answers";
import { copy, dateTime, format } from "../copy";
import { focusById } from "../ui/focus";
import {
  Button,
  ConnectionNotice,
  ErrorMessage,
  Message,
  Notice,
  secondaryButton,
  Skeleton,
  Spinner,
  StatusPill,
  th,
} from "../ui/primitives";
import { pillVariant } from "../ui/statuses";
import {
  inFlight,
  LIST_TITLE_ID,
  regulationOf,
  requestedRow,
  resultText,
  runRefusal,
  statusCaption,
  statusLabel,
  type Refusal,
} from "./inspections";

/**
 * The selected establishment's inspections in one card: header with Run inspection, the run
 * notice (why Run is unavailable or was refused), the connection notice, then the table. Mounted
 * per establishment, so a refusal clears when the establishment changes.
 */
export function InspectionList({
  establishment,
  me,
  live,
  onOpen,
  onStarted,
}: {
  establishment: Establishment;
  me: Me | undefined;
  live: Live;
  /** A row or View progress was chosen (its link navigates). */
  onOpen: () => void;
  /** A run was accepted: the screen opens it. */
  onStarted: (id: string) => void;
}) {
  const queryClient = useQueryClient();
  const inspections = useInspections(establishment.id, true);
  const canRun = can(me, permissions.inspections.run);
  const failed = queryFailed(inspections);
  const retrying = failed && inspections.isFetching;
  const count = inspections.data?.length ?? 0;
  const running = inspections.data?.find((row) => inFlight(row.status));
  const regulation = regulationOf(establishment);
  const { send } = useSend();
  const [posting, setPosting] = useState(false);
  const [refusal, setRefusal] = useState<Refusal>();
  const [now, setNow] = useState(Date.now);
  const bodyId = useId();

  // A quota or a rate limit holds Run until its time, then the notice clears; meanwhile its
  // "in 6 hours" counts down every minute.
  const until = refusal && "until" in refusal ? refusal.until : undefined;
  useEffect(() => {
    if (until === undefined) return;
    const tick = setInterval(() => setNow(Date.now()), 60_000);
    const reset = setTimeout(() => setRefusal(undefined), Math.max(0, until - Date.now()));
    return () => {
      clearInterval(tick);
      clearTimeout(reset);
    };
  }, [until]);

  const blocked = canRun && (running !== undefined || until !== undefined);

  // Run goes when /me stops granting it (a 403): focus, lost with the button, goes to the heading.
  const hadRun = useRef(canRun);
  useEffect(() => {
    if (hadRun.current && !canRun && document.activeElement === document.body)
      focusById(LIST_TITLE_ID);
    hadRun.current = canRun;
  }, [canRun]);

  const run = async () => {
    setRefusal(undefined);
    setPosting(true);
    const result = await send(
      `/establishments/${encodeURIComponent(establishment.id)}/inspections`,
      { method: "POST" },
    );
    const { id } = result.ok
      ? ((await result.response.json().catch(() => ({}))) as { id?: unknown })
      : {};
    setPosting(false);
    const key = inspectionsKey(establishment.id);
    if (typeof id === "string") {
      // The row shows at once, as Waiting; the refetch brings the server's.
      queryClient.setQueryData<InspectionSummary[]>(key, (list = []) => [
        requestedRow(id, regulation),
        ...list.filter((row) => row.id !== id),
      ]);
      void queryClient.invalidateQueries({ queryKey: key });
      onStarted(id);
      return;
    }
    const at = Date.now();
    // An accepted start without its id counts as a failed one.
    const next = runRefusal(result.ok ? { ok: false, status: 0, code: undefined } : result, at);
    // A stale list: the refetch shows the running row, and with it the running notice.
    if (next === "running") {
      void queryClient.invalidateQueries({ queryKey: key });
      return;
    }
    setNow(at);
    setRefusal(next);
  };

  // Try again: once it loads the button goes away, so focus moves to the heading.
  const reload = async () => {
    if ((await inspections.refetch()).isSuccess) focusById(LIST_TITLE_ID);
  };

  // At most one notice: a running inspection first, then the last refusal. A refusal still shows
  // once /me no longer grants Run (a 403), so the button's disappearance is explained.
  let notice: ReactNode = null;
  if (canRun && running) {
    notice = (
      <Notice
        className="mx-5 mb-4"
        icon={<Spinner />}
        title={copy.inspections.runningTitle}
        action={
          <Link to={`/inspections/${running.id}`} onClick={onOpen} className={secondaryButton}>
            {copy.inspections.viewProgress}
          </Link>
        }
      >
        <p id={bodyId}>{copy.inspections.runningBody}</p>
      </Notice>
    );
  } else if (refusal) {
    notice = (
      <RefusalNotice
        refusal={refusal}
        now={now}
        bodyId={bodyId}
        establishment={establishment}
        canReadDocuments={can(me, permissions.documents.read)}
      />
    );
  }

  return (
    <section
      aria-labelledby={LIST_TITLE_ID}
      aria-busy={(!failed && inspections.isPending) || retrying}
      className="rounded-card border border-border bg-card motion-safe:animate-fade-in"
    >
      <div className="flex flex-wrap items-start justify-between gap-3 p-5">
        <div className="min-w-0">
          <h2 id={LIST_TITLE_ID} tabIndex={-1} className="text-title font-semibold break-words">
            {establishment.name}
          </h2>
          {inspections.data ? (
            <p className="text-small text-text-secondary tabular-nums">
              {count === 1
                ? copy.inspections.oneInspection
                : format(copy.inspections.count, { count: String(count) })}
            </p>
          ) : (
            !failed && <Skeleton className="mt-1.5 h-3 w-24" />
          )}
        </div>
        {canRun && (
          // aria-disabled, not disabled: it stays focusable and names its reason.
          <Button
            variant="primary"
            className={`w-full aria-disabled:hover:bg-mint sm:w-auto ${blocked ? "opacity-50" : ""}`}
            aria-disabled={blocked || posting}
            aria-describedby={blocked ? bodyId : undefined}
            onClick={blocked || posting ? undefined : () => void run()}
          >
            {posting ? <Spinner surface="mint" /> : <Play aria-hidden="true" className="size-4" />}
            {posting ? copy.inspections.starting : copy.inspections.run}
          </Button>
        )}
      </div>
      <div role="status">{notice}</div>
      <ConnectionNotice live={live} labels={copy.inspections} />

      {failed ? (
        <div className="px-5 pb-5">
          <ErrorMessage
            title={copy.inspections.errorTitle}
            body={copy.inspections.errorBody}
            retryLabel={copy.inspections.tryAgain}
            retrying={retrying}
            onRetry={() => void reload()}
          />
        </div>
      ) : !inspections.data ? (
        <ListSkeleton />
      ) : count === 0 ? (
        <div className="px-5 pb-5">
          <Message
            className="items-start"
            icon={<ClipboardCheck aria-hidden="true" className="size-5 text-neutral" />}
            title={copy.inspections.emptyTitle}
            body={format(canRun ? copy.inspections.emptyBody : copy.inspections.emptyBodyReadOnly, {
              establishment: establishment.name,
              regulation,
            })}
          />
        </div>
      ) : (
        <InspectionTable
          establishment={establishment}
          inspections={inspections.data}
          onOpen={onOpen}
        />
      )}
    </section>
  );
}

function RefusalNotice({
  refusal,
  now,
  bodyId,
  establishment,
  canReadDocuments,
}: {
  refusal: Refusal;
  now: number;
  bodyId: string;
  establishment: Establishment;
  canReadDocuments: boolean;
}) {
  const clock = <Clock aria-hidden="true" className="size-4" />;
  const alert = <CircleAlert aria-hidden="true" className="size-4" />;
  const body = (text: string) => <p id={bodyId}>{text}</p>;
  switch (refusal.kind) {
    case "quota":
    case "rateLimited": {
      const when = untilReset(refusal.until, now);
      const quota = refusal.kind === "quota";
      return (
        <Notice
          className="mx-5 mb-4"
          icon={clock}
          title={quota ? copy.inspections.quotaTitle : copy.inspections.rateLimitedTitle}
        >
          {body(
            format(quota ? copy.inspections.quotaBody : copy.inspections.rateLimitedBody, {
              when,
            }),
          )}
        </Notice>
      );
    }
    case "noDocuments":
      return (
        <Notice
          className="mx-5 mb-4"
          icon={<FileText aria-hidden="true" className="size-4" />}
          title={format(copy.inspections.noDocumentsTitle, { establishment: establishment.name })}
          action={
            canReadDocuments && (
              <Link to="/documents" state={{ focusTitle: true }} className={secondaryButton}>
                {copy.inspections.goToDocuments}
              </Link>
            )
          }
        >
          {body(copy.inspections.noDocumentsBody)}
        </Notice>
      );
    case "capacity":
      return (
        <Notice className="mx-5 mb-4" icon={clock} title={copy.inspections.capacityTitle}>
          {body(copy.inspections.capacityBody)}
        </Notice>
      );
    case "denied":
      return <Notice className="mx-5 mb-4" icon={alert} title={copy.inspections.denied} />;
    case "error":
      return (
        <Notice className="mx-5 mb-4" icon={alert} title={copy.inspections.startErrorTitle}>
          {body(copy.inspections.startErrorBody)}
        </Notice>
      );
  }
}

function InspectionTable({
  establishment,
  inspections,
  onOpen,
}: {
  establishment: Establishment;
  inspections: InspectionSummary[];
  onOpen: () => void;
}) {
  const id = useId();
  return (
    <table className="w-full">
      <caption className="sr-only">
        {format(copy.inspections.tableCaption, { establishment: establishment.name })}
      </caption>
      <thead className="bg-inset">
        <tr>
          <th scope="col" className={`${th} pl-5`}>
            {copy.inspections.requested}
          </th>
          {/* The last column below 768px. */}
          <th scope="col" className={`${th} pr-5 md:pr-3`}>
            {copy.inspections.status}
          </th>
          <th scope="col" className={`${th} hidden pr-5 md:table-cell`}>
            {copy.inspections.results}
          </th>
        </tr>
      </thead>
      <tbody>
        {inspections.map((row) => {
          const statusId = `${id}-${row.id}`;
          const caption = statusCaption(row);
          const results = resultText(row);
          return (
            <tr key={row.id} className="relative h-14 border-t border-border hover:bg-elevated">
              {/* The date truncates below 768px; from there Results takes the rest. */}
              <td className="w-full max-w-0 py-2.5 pr-3 pl-5 md:w-auto md:max-w-none md:whitespace-nowrap">
                {/* Stretched over the row: the whole row opens it, with one tab stop. */}
                <Link
                  to={`/inspections/${row.id}`}
                  onClick={onOpen}
                  aria-describedby={statusId}
                  className="block truncate text-body font-medium tabular-nums after:absolute after:inset-0 focus-visible:outline-none focus-visible:after:outline-2 focus-visible:after:-outline-offset-2 focus-visible:after:outline-mint"
                >
                  <time dateTime={row.requestedAt}>{dateTime(row.requestedAt)}</time>
                </Link>
                {results && (
                  <span className="block truncate text-small text-text-secondary tabular-nums md:hidden">
                    {results}
                  </span>
                )}
              </td>
              <td id={statusId} className="py-2.5 pr-5 pl-3 md:pr-3 md:whitespace-nowrap">
                <StatusPill
                  variant={pillVariant("inspection", row.status)}
                  label={statusLabel(row.status)}
                />
                {caption && (
                  <>
                    {" "}
                    <span className="mt-0.5 block text-caption text-text-secondary tabular-nums">
                      {caption}
                    </span>
                  </>
                )}
              </td>
              <td className="hidden py-2.5 pr-5 pl-3 text-small text-text-secondary tabular-nums md:table-cell md:w-full">
                {results ?? (
                  <>
                    <span aria-hidden="true">—</span>
                    <span className="sr-only">{copy.inspections.noResults}</span>
                  </>
                )}
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

/** Four rows: a date bar and a pill. */
function ListSkeleton() {
  return (
    <>
      <span className="sr-only">{copy.inspections.loading}</span>
      <ul aria-hidden="true" className="border-t border-border">
        {[0, 1, 2, 3].map((row) => (
          <li
            key={row}
            className="flex h-14 items-center gap-3 border-t border-border px-5 first:border-t-0"
          >
            <span className="flex-1">
              <Skeleton className="h-3 w-2/5" />
            </span>
            <Skeleton className="h-5 w-20" />
          </li>
        ))}
      </ul>
    </>
  );
}
