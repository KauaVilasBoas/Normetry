import { useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, CircleAlert, Clock, Trash2, Upload } from "lucide-react";
import { AlertDialog } from "radix-ui";
import { useEffect, useId, useRef, useState } from "react";
import { Link } from "react-router";
import { ApiError } from "../api/client";
import { permissions } from "../api/contract";
import type { Live } from "../api/live";
import {
  can,
  documentKey,
  documentsKey,
  queryFailed,
  useDocument,
  type DocumentDetail,
  type DocumentSummary,
  type Establishment,
  type Me,
} from "../api/queries";
import { calendarDate, copy, fileSize, format, shortDate } from "../copy";
import { useSend } from "../settings/profiles";
import {
  Button,
  ConnectionNotice,
  dialogPanel,
  FormDialog,
  IngestionPill,
  Notice,
  secondaryButton,
  Skeleton,
  Spinner,
  Tag,
  TextField,
  textAction,
  th,
} from "../ui/primitives";
import {
  cachedTitle,
  expiryProblem,
  failureText,
  inFlight,
  statusLabel,
  typeLabel,
  versionNotice,
} from "./documents";
import { DETAIL_TITLE_ID, ExpiryBadge, VersionText } from "./parts";
import { UploadDialog } from "./UploadDialog";

/**
 * One document: header with Delete and Upload new version, then its versions. Upload new version
 * is `aria-disabled` while the newest version is Waiting or Processing.
 */
export function DocumentDetailView({
  id,
  establishment,
  me,
  live,
  onBack,
  onDeleted,
  announce,
}: {
  id: string;
  establishment: Establishment;
  me: Me | undefined;
  live: Live;
  /** The back link was chosen (it navigates). */
  onBack: () => void;
  onDeleted: (title: string) => void;
  announce: (text: string) => void;
}) {
  const queryClient = useQueryClient();
  const query = useDocument(id, true);
  const status = query.error instanceof ApiError ? query.error.status : 0;
  const notFound = queryFailed(query) && (status === 404 || status === 400);
  const failed = queryFailed(query) && !notFound;
  const retrying = failed && query.isFetching;
  const detail = query.data;
  // The list's row, shown while the document loads.
  const title = detail?.title ?? cachedTitle(queryClient, establishment.id, id);
  const canUpload = can(me, permissions.documents.upload);
  const canDelete = can(me, permissions.documents.delete);
  const busy = detail?.versions[0] !== undefined && inFlight(detail.versions[0]);
  const [uploading, setUploading] = useState(false);
  const uploadButton = useRef<HTMLButtonElement>(null);
  const afterId = useId();

  // Try again: the error card (and its button) goes once the document loads, so focus moves to
  // the title, after the commit that shows it.
  const retried = useRef(false);
  useEffect(() => {
    if (!retried.current || !detail) return;
    retried.current = false;
    document.getElementById(DETAIL_TITLE_ID)?.focus();
  }, [detail]);

  const back = (
    <Link
      to="/documents"
      onClick={onBack}
      className="-mx-1 inline-flex items-center gap-1.5 self-start rounded-sm px-1 text-body text-text-secondary hover:text-text-primary"
    >
      <ArrowLeft aria-hidden="true" className="size-4" />
      {copy.documents.back}
    </Link>
  );

  if (notFound || failed)
    return (
      <div className="flex flex-col gap-3">
        {back}
        <section
          aria-labelledby={DETAIL_TITLE_ID}
          aria-busy={retrying}
          className="w-full max-w-160 rounded-card border border-border bg-card p-5 motion-safe:animate-fade-in"
        >
          <div role={failed ? "alert" : undefined} className="flex flex-col items-start gap-2">
            <CircleAlert aria-hidden="true" className="size-5 text-text-secondary" />
            <h2 id={DETAIL_TITLE_ID} tabIndex={-1} className="text-title font-semibold">
              {notFound ? copy.documents.notFoundTitle : copy.documents.loadErrorTitle}
            </h2>
            <p className="text-body text-text-secondary">
              {notFound ? copy.documents.notFoundBody : copy.documents.errorBody}
            </p>
            {notFound ? (
              <Link to="/documents" onClick={onBack} className={`${secondaryButton} mt-2`}>
                {copy.documents.backToDocuments}
              </Link>
            ) : (
              <Button
                variant="secondary"
                className="mt-2"
                aria-disabled={retrying}
                onClick={
                  retrying
                    ? undefined
                    : () => {
                        retried.current = true;
                        void query.refetch();
                      }
                }
              >
                {retrying && <Spinner />}
                {copy.documents.tryAgain}
              </Button>
            )}
          </div>
        </section>
      </div>
    );

  return (
    <div className="flex flex-col gap-4" aria-busy={!detail}>
      <div className="flex flex-col gap-2">
        {back}
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start">
          <div className="min-w-0 flex-1">
            <h2
              id={DETAIL_TITLE_ID}
              tabIndex={-1}
              className="text-greeting font-semibold break-words"
            >
              {title ?? (
                <>
                  <Skeleton className="my-0.5 h-6 w-64 max-w-full" />
                  <span className="sr-only">{copy.documents.loadingDocument}</span>
                </>
              )}
            </h2>
            {detail ? (
              <>
                <p className="mt-1 text-body text-text-secondary">
                  {format(copy.documents.meta, {
                    type: typeLabel(detail.type),
                    establishment: establishment.name,
                  })}
                </p>
                {/* Without a date, the line is only worth showing beside its edit action. */}
                {(detail.expiresOn || canUpload) && (
                  <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-body">
                    {detail.expiresOn ? (
                      <>
                        <time dateTime={detail.expiresOn}>
                          {format(copy.documents.expiresOn, {
                            date: calendarDate(detail.expiresOn),
                          })}
                        </time>
                        <ExpiryBadge expiresOn={detail.expiresOn} />
                      </>
                    ) : (
                      <span className="text-text-secondary">{copy.documents.noExpiry}</span>
                    )}
                    {canUpload && <ExpiryDialog detail={detail} announce={announce} />}
                  </div>
                )}
              </>
            ) : (
              <>
                <Skeleton className="mt-2 h-3 w-40" />
                <Skeleton className="mt-2 h-3 w-32" />
              </>
            )}
          </div>
          {detail && (canDelete || canUpload) && (
            <div className="flex gap-2">
              {canDelete && (
                <DeleteDocument
                  detail={detail}
                  establishment={establishment}
                  onDeleted={() => {
                    queryClient.removeQueries({ queryKey: documentKey(id) });
                    queryClient.setQueryData<DocumentSummary[]>(
                      documentsKey(establishment.id),
                      (list) => list?.filter((row) => row.id !== id),
                    );
                    void queryClient.invalidateQueries({
                      queryKey: documentsKey(establishment.id),
                    });
                    onDeleted(detail.title);
                  }}
                />
              )}
              {canUpload && (
                <Button
                  ref={uploadButton}
                  variant="primary"
                  aria-disabled={busy}
                  aria-describedby={busy ? afterId : undefined}
                  onClick={busy ? undefined : () => setUploading(true)}
                  className="aria-disabled:opacity-50 aria-disabled:hover:bg-mint"
                >
                  <Upload aria-hidden="true" className="size-4" />
                  {copy.documents.uploadVersion}
                </Button>
              )}
            </div>
          )}
        </div>
      </div>

      {detail ? (
        <Versions detail={detail} live={live} afterId={canUpload ? afterId : undefined} />
      ) : (
        // The heading says it is loading until the title is known; then the skeleton does.
        <VersionsSkeleton label={title !== undefined} />
      )}

      {canUpload && detail && (
        <UploadDialog
          open={uploading}
          onClose={() => setUploading(false)}
          files={[]}
          establishment={establishment}
          existing={detail}
          returnFocus={uploadButton}
          onUploaded={(uploaded) => {
            // Set before the dialog closes, so focus comes back to a button already disabled.
            queryClient.setQueryData(documentKey(id), uploaded);
            void queryClient.invalidateQueries({ queryKey: documentsKey(establishment.id) });
            announce(copy.documents.versionUploaded);
          }}
        />
      )}
    </div>
  );
}

/** The versions card: its notice about the newest version, then the table. */
function Versions({
  detail,
  live,
  afterId,
}: {
  detail: DocumentDetail;
  live: Live;
  /** The id for "You can upload another version…", when the user may upload. */
  afterId: string | undefined;
}) {
  const id = useId();
  const notice = versionNotice(detail);
  const now = new Date();
  return (
    <section
      aria-labelledby={`${id}-title`}
      className="rounded-card border border-border bg-card motion-safe:animate-fade-in"
    >
      <h2 id={`${id}-title`} className="p-5 text-title font-semibold">
        {copy.documents.versions}
      </h2>
      <ConnectionNotice live={live} labels={copy.documents} />
      {notice && (
        <Notice
          className="mx-5 mb-4"
          icon={
            notice.status === "Failed" ? (
              <CircleAlert aria-hidden="true" className="size-4" />
            ) : notice.status === "Processing" ? (
              <Spinner />
            ) : (
              <Clock aria-hidden="true" className="size-4" />
            )
          }
          title={notice.title}
        >
          {notice.lines.map((line) => (
            <p key={line}>{line}</p>
          ))}
          {notice.status !== "Failed" && afterId && (
            <p id={afterId}>{copy.documents.uploadAfter}</p>
          )}
        </Notice>
      )}
      <table className="w-full">
        <thead className="bg-inset">
          <tr>
            <th scope="col" className={`${th} pl-5`}>
              {copy.documents.version}
            </th>
            <th scope="col" className={`${th} hidden md:table-cell`}>
              {copy.documents.file}
            </th>
            {/* The last column below 768px. */}
            <th scope="col" className={`${th} pr-5 md:pr-3`}>
              {copy.documents.status}
            </th>
            <th scope="col" className={`${th} hidden pr-5 md:table-cell`}>
              {copy.documents.uploadedAt}
            </th>
          </tr>
        </thead>
        <tbody>
          {detail.versions.map((version) => {
            const uploader = version.uploadedBy.displayName ?? copy.documents.unknownUploader;
            const date = (
              <time dateTime={version.uploadedAt}>{shortDate(version.uploadedAt, now)}</time>
            );
            const size = fileSize(version.size);
            return (
              <tr key={version.id} className="h-14 border-t border-border">
                {/* Below 768px File and Uploaded become two lines here. */}
                <td className="w-full max-w-0 py-2.5 pr-3 pl-5 md:w-auto md:max-w-none">
                  <span className="flex items-center gap-2 text-body font-medium tabular-nums">
                    <span>
                      <VersionText number={version.number} />
                    </span>
                    {version.number === detail.currentVersionNumber && (
                      <Tag>{copy.documents.current}</Tag>
                    )}
                  </span>
                  <span className="block truncate text-caption text-text-secondary md:hidden">
                    {version.fileName} · {size}
                  </span>
                  <span className="block truncate text-caption text-text-secondary md:hidden">
                    {date} · {uploader}
                  </span>
                </td>
                <td className="hidden max-w-0 px-3 py-2.5 md:table-cell md:w-full">
                  <span className="block truncate text-body">{version.fileName}</span>
                  <span className="block text-small text-text-secondary tabular-nums">{size}</span>
                </td>
                <td className="py-2.5 pr-5 pl-3 md:pr-3">
                  <IngestionPill status={version.status} label={statusLabel(version.status)} />
                  {version.status === "Failed" && (
                    <>
                      {" "}
                      <span className="mt-0.5 block text-caption text-text-secondary">
                        {failureText(version.failureCode).short}
                      </span>
                    </>
                  )}
                </td>
                <td className="hidden py-2.5 pr-5 pl-3 whitespace-nowrap md:table-cell">
                  <span className="block text-small tabular-nums">{date}</span>
                  <span className="block text-small text-text-secondary">{uploader}</span>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </section>
  );
}

function VersionsSkeleton({ label }: { label: boolean }) {
  return (
    <>
      {label && <span className="sr-only">{copy.documents.loadingDocument}</span>}
      <div
        aria-hidden="true"
        className="rounded-card border border-border bg-card motion-safe:animate-fade-in"
      >
        <div className="p-5">
          <Skeleton className="h-4 w-24" />
        </div>
        <ul className="border-t border-border">
          {[0, 1, 2].map((row) => (
            <li
              key={row}
              className="flex h-14 items-center gap-3 border-t border-border px-5 first:border-t-0"
            >
              <span className="flex flex-1 flex-col gap-2">
                <Skeleton className="h-3 w-16" />
                <Skeleton className="h-2.5 w-2/5" />
              </span>
              <Skeleton className="h-5 w-20" />
            </li>
          ))}
        </ul>
      </div>
    </>
  );
}

/**
 * "Edit expiry date" and its dialog (`documents.upload`): Save sends the field, and an empty one
 * clears the date ("Remove date" empties it). The confirmation is announced once the dialog has
 * closed, when the page is heard again; focus goes back to the trigger.
 */
function ExpiryDialog({
  detail,
  announce,
}: {
  detail: DocumentDetail;
  announce: (text: string) => void;
}) {
  const queryClient = useQueryClient();
  // A 403 refetches /me once the dialog closes, so its message stays readable until then.
  const { send, closed } = useSend({ inside: true });
  const [open, setOpen] = useState(false);
  const [value, setValue] = useState("");
  // A new object per refusal, so the same error twice still moves focus to the field.
  const [fieldError, setFieldError] = useState<{ text: string }>();
  const [error, setError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);
  const saved = useRef<string>(undefined);
  const input = useRef<HTMLInputElement>(null);
  const current = detail.expiresOn ?? "";

  useEffect(() => {
    if (fieldError) input.current?.focus();
  }, [fieldError]);

  const save = async (expiresOn: string | null) => {
    setError(undefined);
    setSubmitting(true);
    const result = await send(
      `/establishments/${encodeURIComponent(detail.establishmentId)}/documents/${encodeURIComponent(detail.id)}`,
      { method: "PATCH", json: { expiresOn } },
    );
    setSubmitting(false);
    if (!result.ok) {
      if (result.code === "validation") setFieldError({ text: copy.upload.expiryInvalid });
      else setError(result.status === 403 ? copy.settings.denied : copy.documents.expiryError);
      return;
    }
    queryClient.setQueryData<DocumentDetail>(
      documentKey(detail.id),
      (cached) => cached && { ...cached, expiresOn },
    );
    void queryClient.invalidateQueries({ queryKey: documentKey(detail.id) });
    void queryClient.invalidateQueries({ queryKey: documentsKey(detail.establishmentId) });
    saved.current = expiresOn ? copy.documents.expirySaved : copy.documents.expiryRemoved;
    setOpen(false);
  };

  return (
    <FormDialog
      trigger={
        <button type="button" className={textAction}>
          {copy.documents.editExpiry}
        </button>
      }
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) {
          closed();
          return;
        }
        setValue(current);
        setFieldError(undefined);
        setError(undefined);
      }}
      title={copy.documents.editExpiry}
      submitLabel={submitting ? copy.documents.saving : copy.documents.save}
      cancelLabel={copy.documents.cancel}
      submitting={submitting}
      ready={value !== current}
      error={error}
      onSubmit={() => {
        const problem = expiryProblem(input.current);
        if (problem) setFieldError({ text: problem });
        else void save(value || null);
      }}
      onCloseAutoFocus={() => {
        if (saved.current) announce(saved.current);
        saved.current = undefined;
      }}
    >
      <div className="flex flex-col items-start gap-2">
        <div className="w-full">
          <TextField
            ref={input}
            type="date"
            max="9999-12-31"
            label={copy.upload.expiryLabel}
            hint={copy.upload.expiryHint}
            error={fieldError?.text}
            value={value}
            onChange={(event) => {
              setValue(event.target.value);
              setFieldError(undefined);
            }}
          />
        </div>
        {detail.expiresOn && value && (
          <button
            type="button"
            className={textAction}
            onClick={() => {
              // Focus moves first: the button goes once the field is empty.
              input.current?.focus();
              setValue("");
              setFieldError(undefined);
            }}
          >
            {copy.documents.removeExpiry}
          </button>
        )}
      </div>
    </FormDialog>
  );
}

/** The header's Delete and its confirmation; no red button (DESIGN.md). */
function DeleteDocument({
  detail,
  establishment,
  onDeleted,
}: {
  detail: DocumentDetail;
  establishment: Establishment;
  onDeleted: () => void;
}) {
  // A 403 refetches /me once the dialog closes, so its message stays readable until then.
  const { send, closed } = useSend({ inside: true });
  const [open, setOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string>();
  const trigger = useRef<HTMLButtonElement>(null);

  const confirm = async () => {
    setDeleting(true);
    setError(undefined);
    const result = await send(`/documents/${encodeURIComponent(detail.id)}`, {
      method: "DELETE",
    });
    // 404: it is already gone, which is what the user asked for.
    if (result.ok || result.status === 404) {
      onDeleted();
      return;
    }
    setDeleting(false);
    setError(result.status === 403 ? copy.settings.denied : copy.documents.deleteError);
  };

  return (
    <AlertDialog.Root
      open={open}
      onOpenChange={(next) => {
        if (deleting) return;
        setOpen(next);
        setError(undefined);
        if (!next) closed();
      }}
    >
      <AlertDialog.Trigger asChild>
        <Button ref={trigger} variant="secondary" aria-label={copy.documents.deleteLabel}>
          <Trash2 aria-hidden="true" className="size-4" />
          {copy.documents.delete}
        </Button>
      </AlertDialog.Trigger>
      <AlertDialog.Portal>
        <AlertDialog.Overlay className="fixed inset-0 z-40 bg-scrim" />
        <AlertDialog.Content
          // Back to the trigger after Cancel; after a delete the list heading takes focus.
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            if (trigger.current?.isConnected) trigger.current.focus();
          }}
          className={dialogPanel}
        >
          <AlertDialog.Title className="text-title font-semibold">
            {format(copy.documents.deleteTitle, { title: detail.title })}
          </AlertDialog.Title>
          <AlertDialog.Description className="mt-2 text-body text-text-secondary">
            {format(copy.documents.deleteBody, { establishment: establishment.name })}
          </AlertDialog.Description>
          {error && (
            <p role="alert" className="mt-3 text-body">
              {error}
            </p>
          )}
          <div className="mt-5 flex justify-end gap-2">
            <AlertDialog.Cancel asChild>
              <Button variant="secondary" onElevated>
                {copy.documents.cancel}
              </Button>
            </AlertDialog.Cancel>
            <Button
              variant="primary"
              aria-disabled={deleting}
              onClick={deleting ? undefined : () => void confirm()}
            >
              {deleting && <Spinner surface="mint" />}
              {copy.documents.deleteConfirm}
            </Button>
          </div>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  );
}
