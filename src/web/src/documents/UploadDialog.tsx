import { Dialog } from "radix-ui";
import type { DragEvent, RefObject } from "react";
import type { RequestOptions } from "../api/client";
import type { DocumentDetail } from "../api/documents";
import type { Establishment } from "../api/queries";
import { useSend, type Sent } from "../api/send";
import { copy, fileSize, format } from "../copy";
import {
  Button,
  DropZone,
  SelectField,
  Spinner,
  TextField,
  uploadDialogPanel,
} from "../ui/primitives";
import { ACCEPT, TITLE_MAX_LENGTH } from "./documents";
import { useUploadForm } from "./upload";

// A file dropped beside the drop zone would make the browser open it, leaving the app (and its
// in-memory session). Inside the dialog it does nothing, and it never reaches the list card.
const swallowFiles = (event: DragEvent) => {
  if (!event.dataTransfer.types.includes("Files")) return;
  event.preventDefault();
  event.stopPropagation();
};

const typeOptions = Object.entries(copy.documents.typeOptions).map(([value, label]) => ({
  value,
  label,
}));

/**
 * Upload a document, or a new version of `existing` (spec §2, §5). The form mounts with each
 * opening, so it starts empty, or with the `files` dropped on the list. Closing it (Cancel, Esc)
 * aborts a running upload. Focus goes back to `returnFocus`.
 */
export function UploadDialog({
  open,
  onClose,
  files,
  establishment,
  existing,
  returnFocus,
  onUploaded,
}: {
  open: boolean;
  onClose: () => void;
  files: File[];
  establishment: Establishment;
  /** The document a new version is for; a new document when undefined. */
  existing?: DocumentDetail;
  returnFocus: RefObject<HTMLButtonElement | null>;
  onUploaded: (document: DocumentDetail) => void;
}) {
  // A 403 refetches /me once the dialog closes, so its message stays readable until then.
  const { send, closed } = useSend({ inside: true });
  return (
    <Dialog.Root
      open={open}
      onOpenChange={(next) => {
        if (next) return;
        onClose();
        closed();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay
          onDragOver={swallowFiles}
          onDrop={swallowFiles}
          className="fixed inset-0 z-40 bg-scrim"
        />
        <Dialog.Content
          // Only Cancel and Esc close it: a stray click on the scrim (back from finding the file)
          // would abort the upload and lose the form.
          onPointerDownOutside={(event) => event.preventDefault()}
          onDragOver={swallowFiles}
          onDrop={swallowFiles}
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            returnFocus.current?.focus();
          }}
          className={uploadDialogPanel}
        >
          <UploadForm
            files={files}
            establishment={establishment}
            existing={existing}
            send={send}
            onUploaded={(uploaded) => {
              onUploaded(uploaded);
              onClose();
            }}
          />
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

/** The form inside the dialog (`useUploadForm`): the drop zone, then the new document's fields. */
function UploadForm({
  files,
  establishment,
  existing,
  send,
  onUploaded,
}: {
  files: File[];
  establishment: Establishment;
  existing: DocumentDetail | undefined;
  send: (path: string, options: RequestOptions) => Promise<Sent>;
  onUploaded: (document: DocumentDetail) => void;
}) {
  const {
    file,
    title,
    type,
    expiresOn,
    errors,
    error,
    chosen,
    submitting,
    zone,
    titleInput,
    typeSelect,
    expiryInput,
    choose,
    changeTitle,
    changeType,
    changeExpiresOn,
    submit,
  } = useUploadForm({ files, establishment, existing, send, onUploaded });

  const current = existing?.currentVersionNumber ?? null;
  return (
    <>
      <Dialog.Title className="text-title font-semibold">
        {existing ? copy.upload.versionTitle : copy.upload.title}
      </Dialog.Title>
      <Dialog.Description className="mt-1 text-body text-text-secondary">
        {!existing
          ? format(copy.upload.description, { establishment: establishment.name })
          : current !== null
            ? format(copy.upload.versionDescription, { title: existing.title, c: String(current) })
            : format(copy.upload.versionDescriptionNoCurrent, { title: existing.title })}
      </Dialog.Description>
      <form noValidate className="mt-4 flex flex-col gap-4" onSubmit={submit}>
        <DropZone
          ref={zone}
          file={file}
          size={file ? fileSize(file.size) : ""}
          accept={ACCEPT}
          label={copy.upload.zone}
          draggingLabel={copy.upload.zoneDragging}
          hint={copy.upload.zoneHint}
          another={copy.upload.another}
          error={errors.file}
          onFiles={choose}
        />
        {!existing && (
          <>
            <TextField
              ref={titleInput}
              label={copy.upload.titleLabel}
              error={errors.title}
              value={title}
              maxLength={TITLE_MAX_LENGTH}
              autoComplete="off"
              onChange={changeTitle}
            />
            <SelectField
              ref={typeSelect}
              label={copy.upload.typeLabel}
              placeholder={copy.upload.typePlaceholder}
              options={typeOptions}
              error={errors.type}
              value={type}
              onChange={changeType}
            />
            <TextField
              ref={expiryInput}
              type="date"
              max="9999-12-31"
              label={copy.upload.expiryLabel}
              hint={copy.upload.expiryHint}
              error={errors.expiresOn}
              value={expiresOn}
              onChange={changeExpiresOn}
            />
          </>
        )}
        {error && (
          <p role="alert" className="text-body">
            {error}
          </p>
        )}
        <div className="mt-1 flex justify-end gap-2">
          <Dialog.Close asChild>
            <Button variant="secondary" onElevated>
              {copy.upload.cancel}
            </Button>
          </Dialog.Close>
          <Button type="submit" variant="primary" aria-disabled={submitting}>
            {submitting && <Spinner surface="mint" />}
            {submitting ? copy.upload.submitting : copy.upload.submit}
          </Button>
        </div>
      </form>
      {/* Inside the dialog: Radix hides everything outside it while it is open. */}
      <p aria-live="polite" className="sr-only">
        {chosen}
      </p>
    </>
  );
}
