import { useQueryClient } from "@tanstack/react-query";
import { FileText, Upload } from "lucide-react";
import { useId, useRef, useState, type DragEvent } from "react";
import { Link } from "react-router";
import type { Live } from "../api/live";
import {
  can,
  documentsKey,
  queryFailed,
  useDocuments,
  type DocumentSummary,
  type Establishment,
  type ExpiryFilter,
  type Me,
} from "../api/queries";
import { copy, format, shortDate } from "../copy";
import {
  Button,
  ConnectionNotice,
  ErrorMessage,
  FilterSelect,
  IngestionPill,
  Message,
  Skeleton,
  th,
} from "../ui/primitives";
import { expiry, rowCaption, statusLabel, toSummary, typeLabel } from "./documents";
import { ExpiryBadge, LIST_TITLE_ID, VersionText } from "./parts";
import { UploadDialog } from "./UploadDialog";

const draggingFiles = (event: DragEvent) => event.dataTransfer.types.includes("Files");

const filterOptions = Object.entries(copy.documents.expiryFilters).map(([value, label]) => ({
  value,
  label,
}));

/**
 * The selected establishment's documents in one card: header with the expiry filter and Upload,
 * the connection notice, then the table. With `documents.upload` a file dropped on the card opens
 * the upload dialog with it (a pointer shortcut; Upload document is the keyboard path). The
 * `filter` asks the API for expired or expiring documents only.
 */
export function DocumentList({
  establishment,
  me,
  live,
  filter,
  onFilter,
  onOpen,
  announce,
}: {
  establishment: Establishment;
  me: Me | undefined;
  live: Live;
  filter: ExpiryFilter;
  onFilter: (filter: ExpiryFilter) => void;
  /** A row was chosen (its link navigates). */
  onOpen: () => void;
  announce: (text: string) => void;
}) {
  const queryClient = useQueryClient();
  const documents = useDocuments(establishment.id, true, filter);
  const canUpload = can(me, "documents.upload");
  const failed = queryFailed(documents);
  const retrying = failed && documents.isFetching;
  const [upload, setUpload] = useState<{ files: File[] }>();
  const [dragging, setDragging] = useState(false);
  const uploadButton = useRef<HTMLButtonElement>(null);
  const count = documents.data?.length ?? 0;
  // Nothing to filter in an establishment without documents.
  const filterable = filter !== "" || count > 0;

  const over = (event: DragEvent) => {
    if (!canUpload || !draggingFiles(event)) return;
    event.preventDefault();
    setDragging(true);
  };

  // Try again: once it loads the button goes away, so focus moves to the heading.
  const reload = async () => {
    if ((await documents.refetch()).isSuccess) document.getElementById(LIST_TITLE_ID)?.focus();
  };

  return (
    <section
      aria-labelledby={LIST_TITLE_ID}
      aria-busy={(!failed && documents.isPending) || retrying}
      onDragEnter={over}
      onDragOver={over}
      onDragLeave={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragging(false);
      }}
      onDrop={(event) => {
        if (!canUpload || !draggingFiles(event)) return;
        event.preventDefault();
        setDragging(false);
        setUpload({ files: [...event.dataTransfer.files] });
      }}
      className={`rounded-card border bg-card motion-safe:animate-fade-in ${dragging ? "border-mint" : "border-border"}`}
    >
      <div className="flex flex-wrap items-start justify-between gap-3 p-5">
        <div className="min-w-0">
          <h2 id={LIST_TITLE_ID} tabIndex={-1} className="text-title font-semibold break-words">
            {establishment.name}
          </h2>
          {documents.data ? (
            <p className="text-small text-text-secondary tabular-nums">
              {count === 1
                ? copy.documents.oneDocument
                : format(copy.documents.count, { count: String(count) })}
            </p>
          ) : (
            !failed && <Skeleton className="mt-1.5 h-3 w-24" />
          )}
        </div>
        {(filterable || canUpload) && (
          <div className="flex w-full flex-wrap gap-2 sm:w-auto">
            {filterable && (
              <FilterSelect
                label={copy.documents.expiryFilter}
                options={filterOptions}
                value={filter}
                onChange={(event) => onFilter(event.target.value as ExpiryFilter)}
              />
            )}
            {canUpload && (
              <Button
                ref={uploadButton}
                variant="primary"
                className="w-full sm:w-auto"
                onClick={() => setUpload({ files: [] })}
              >
                <Upload aria-hidden="true" className="size-4" />
                {copy.documents.upload}
              </Button>
            )}
          </div>
        )}
      </div>
      <ConnectionNotice live={live} labels={copy.documents} />

      {failed ? (
        <div className="px-5 pb-5">
          <ErrorMessage
            title={copy.documents.errorTitle}
            body={copy.documents.errorBody}
            retryLabel={copy.documents.tryAgain}
            retrying={retrying}
            onRetry={() => void reload()}
          />
        </div>
      ) : !documents.data ? (
        <ListSkeleton />
      ) : count === 0 && filter !== "" ? (
        <div className="px-5 pb-5">
          <Message
            className="items-start"
            title={
              filter === "expired" ? copy.documents.noExpiredTitle : copy.documents.noExpiringTitle
            }
            body={
              filter === "expired" ? copy.documents.noExpiredBody : copy.documents.noExpiringBody
            }
          />
          <Button
            variant="secondary"
            className="mt-4"
            onClick={() => {
              // The button goes with the filter: focus moves to the heading first.
              document.getElementById(LIST_TITLE_ID)?.focus();
              onFilter("");
            }}
          >
            {copy.documents.showAll}
          </Button>
        </div>
      ) : count === 0 ? (
        <div className="px-5 pb-5">
          <Message
            className="items-start"
            icon={<FileText aria-hidden="true" className="size-5 text-neutral" />}
            title={copy.documents.emptyTitle}
            body={format(canUpload ? copy.documents.emptyBody : copy.documents.emptyBodyReadOnly, {
              establishment: establishment.name,
            })}
          />
        </div>
      ) : (
        <DocumentTable establishment={establishment} documents={documents.data} onOpen={onOpen} />
      )}

      {canUpload && (
        <UploadDialog
          open={upload !== undefined}
          onClose={() => setUpload(undefined)}
          files={upload?.files ?? []}
          establishment={establishment}
          returnFocus={uploadButton}
          onUploaded={(uploaded) => {
            // The row shows at once, as Waiting; the refetch brings the server's order.
            queryClient.setQueryData<DocumentSummary[]>(
              documentsKey(establishment.id),
              (list = []) => [toSummary(uploaded), ...list.filter((row) => row.id !== uploaded.id)],
            );
            void queryClient.invalidateQueries({ queryKey: documentsKey(establishment.id) });
            announce(format(copy.documents.uploaded, { title: uploaded.title }));
          }}
        />
      )}
    </section>
  );
}

function DocumentTable({
  establishment,
  documents,
  onOpen,
}: {
  establishment: Establishment;
  documents: DocumentSummary[];
  onOpen: () => void;
}) {
  const id = useId();
  const now = new Date();
  return (
    <table className="w-full">
      <caption className="sr-only">
        {format(copy.documents.tableCaption, { establishment: establishment.name })}
      </caption>
      <thead className="bg-inset">
        <tr>
          <th scope="col" className={`${th} pl-5`}>
            {copy.documents.document}
          </th>
          <th scope="col" className={`${th} hidden md:table-cell`}>
            {copy.documents.type}
          </th>
          {/* The last column below 768px. */}
          <th scope="col" className={`${th} pr-5 md:pr-3`}>
            {copy.documents.status}
          </th>
          <th scope="col" className={`${th} hidden md:table-cell`}>
            {copy.documents.version}
          </th>
          <th scope="col" className={`${th} hidden md:table-cell`}>
            {copy.documents.expires}
          </th>
          <th scope="col" className={`${th} hidden pr-5 md:table-cell`}>
            {copy.documents.updated}
          </th>
        </tr>
      </thead>
      <tbody>
        {documents.map((row) => {
          const statusId = `${id}-${row.id}`;
          const expiryId = `${statusId}-expiry`;
          const caption = rowCaption(row);
          const expires = row.expiresOn ? expiry(row.expiresOn, now) : undefined;
          const updated = <time dateTime={row.updatedAt}>{shortDate(row.updatedAt, now)}</time>;
          return (
            <tr key={row.id} className="relative h-14 border-t border-border hover:bg-elevated">
              {/* The title truncates; the other columns keep their content. */}
              <td className="w-full max-w-0 py-2.5 pr-3 pl-5">
                {/* Stretched over the row: the whole row opens it, with one tab stop. */}
                <Link
                  to={`/documents/${row.id}`}
                  onClick={onOpen}
                  // Only an urgent date joins the row's name: a bare later date reads as noise.
                  aria-describedby={
                    expires && expires.state !== "later" ? `${statusId} ${expiryId}` : statusId
                  }
                  className="block truncate text-body font-medium after:absolute after:inset-0 focus-visible:outline-none focus-visible:after:outline-2 focus-visible:after:-outline-offset-2 focus-visible:after:outline-mint"
                >
                  {row.title}
                </Link>
                <span className="hidden truncate text-small text-text-secondary md:block">
                  {row.latestVersion.fileName}
                </span>
                <span className="block truncate text-small text-text-secondary tabular-nums md:hidden">
                  {typeLabel(row.type)} · <VersionText number={row.currentVersionNumber} /> ·{" "}
                  {updated}
                </span>
                {/* Below 768px the Expires column is hidden; an expired or expiring date shows here. */}
                {row.expiresOn && expires?.state !== "later" && (
                  <span className="mt-1 flex md:hidden">
                    <ExpiryBadge expiresOn={row.expiresOn} />
                  </span>
                )}
              </td>
              <td className="hidden px-3 py-2.5 text-body whitespace-nowrap text-text-secondary md:table-cell">
                {typeLabel(row.type)}
              </td>
              <td id={statusId} className="py-2.5 pr-5 pl-3 md:pr-3">
                <IngestionPill
                  status={row.latestVersion.status}
                  label={statusLabel(row.latestVersion.status)}
                />
                {caption && (
                  <>
                    {" "}
                    <span className="mt-0.5 block text-caption text-text-secondary">{caption}</span>
                  </>
                )}
              </td>
              <td className="hidden px-3 py-2.5 text-body whitespace-nowrap tabular-nums md:table-cell">
                <VersionText number={row.currentVersionNumber} />
              </td>
              <td id={expiryId} className="hidden px-3 py-2.5 whitespace-nowrap md:table-cell">
                {!row.expiresOn || !expires ? (
                  <>
                    <span aria-hidden="true" className="text-body">
                      —
                    </span>
                    <span className="sr-only">{copy.documents.noExpiry}</span>
                  </>
                ) : expires.state === "later" ? (
                  <time
                    dateTime={row.expiresOn}
                    className="text-small text-text-secondary tabular-nums"
                  >
                    {expires.label}
                  </time>
                ) : (
                  <ExpiryBadge expiresOn={row.expiresOn} />
                )}
              </td>
              <td className="hidden py-2.5 pr-5 pl-3 text-small whitespace-nowrap text-text-secondary tabular-nums md:table-cell">
                {updated}
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

/** Five rows: the title and its second line, and a pill. */
function ListSkeleton() {
  return (
    <>
      <span className="sr-only">{copy.documents.loading}</span>
      <ul aria-hidden="true" className="border-t border-border">
        {[0, 1, 2, 3, 4].map((row) => (
          <li
            key={row}
            className="flex h-14 items-center gap-3 border-t border-border px-5 first:border-t-0"
          >
            <span className="flex flex-1 flex-col gap-2">
              <Skeleton className="h-3 w-2/5" />
              <Skeleton className="h-2.5 w-1/4" />
            </span>
            <Skeleton className="h-5 w-20" />
          </li>
        ))}
      </ul>
    </>
  );
}
