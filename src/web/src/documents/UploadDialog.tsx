import { useQueryClient } from "@tanstack/react-query";
import { Dialog } from "radix-ui";
import { useEffect, useRef, useState, type DragEvent, type RefObject } from "react";
import type { RequestOptions } from "../api/client";
import { problemCodes } from "../api/contract";
import { documentKey, documentsKey, type DocumentDetail } from "../api/documents";
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
import {
  ACCEPT,
  expiryProblem,
  fileProblem,
  TITLE_MAX_LENGTH,
  titleFromFile,
  uploadRefusal,
  withContentType,
} from "./documents";

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

type Field = "file" | "title" | "type" | "expiresOn";

const fieldOrder = ["file", "title", "type", "expiresOn"] as const;

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
  const queryClient = useQueryClient();
  // Files dropped on the list card count as chosen.
  const dropped = files.length > 0 ? fileProblem(files) : undefined;
  const [file, setFile] = useState(files.length > 0 && !dropped ? files[0] : undefined);
  const [title, setTitle] = useState(file ? titleFromFile(file.name) : "");
  const [typed, setTyped] = useState(false);
  const [type, setType] = useState("");
  const [expiresOn, setExpiresOn] = useState("");
  const [errors, setErrors] = useState<Partial<Record<Field, string>>>(
    dropped ? { file: dropped } : {},
  );
  const [error, setError] = useState<string>();
  const [chosen, setChosen] = useState("");
  const [submitting, setSubmitting] = useState(false);
  // A new object per request, so the same field twice still takes focus.
  const [focusRequest, setFocusRequest] = useState<{ field: Field }>();
  const zone = useRef<HTMLButtonElement>(null);
  const titleInput = useRef<HTMLInputElement>(null);
  const typeSelect = useRef<HTMLSelectElement>(null);
  const expiryInput = useRef<HTMLInputElement>(null);
  const upload = useRef<AbortController>(null);

  // Focus moves once the error is rendered, so the error is read as the field's description.
  useEffect(() => {
    if (!focusRequest) return;
    const fields = { file: zone, title: titleInput, type: typeSelect, expiresOn: expiryInput };
    fields[focusRequest.field].current?.focus();
  }, [focusRequest]);

  // Closing the dialog aborts a running upload; the server may have accepted it already.
  useEffect(
    () => () => {
      if (!upload.current) return;
      upload.current.abort();
      void queryClient.invalidateQueries({ queryKey: documentsKey(establishment.id) });
    },
    [queryClient, establishment.id],
  );

  const choose = (next: File[]) => {
    const problem = fileProblem(next);
    setError(undefined);
    if (problem) {
      setFile(undefined);
      setErrors((current) => ({ ...current, file: problem }));
      setChosen("");
      return;
    }
    const [picked] = next;
    setFile(picked);
    setErrors((current) => ({ ...current, file: undefined }));
    if (!typed) setTitle(titleFromFile(picked.name));
    setChosen(format(copy.upload.chosen, { file: picked.name }));
  };

  const submit = async () => {
    if (submitting) return;
    setError(undefined);
    const found: Partial<Record<Field, string>> = {
      file: file ? undefined : (errors.file ?? copy.upload.fileRequired),
      title: existing || title.trim() ? undefined : copy.upload.titleRequired,
      type: existing || type ? undefined : copy.upload.typeRequired,
      expiresOn: existing ? undefined : expiryProblem(expiryInput.current),
    };
    setErrors(found);
    const first = fieldOrder.find((field) => found[field]);
    if (first || !file) {
      setFocusRequest({ field: first ?? "file" });
      return;
    }

    const form = new FormData();
    form.append("file", withContentType(file));
    if (!existing) {
      form.append("title", title.trim());
      form.append("type", type);
      if (expiresOn) form.append("expiresOn", expiresOn);
    }
    const controller = new AbortController();
    upload.current = controller;
    setSubmitting(true);
    const result = await send(
      existing
        ? `/documents/${encodeURIComponent(existing.id)}/versions`
        : `/establishments/${encodeURIComponent(establishment.id)}/documents`,
      { method: "POST", form, signal: controller.signal },
    );
    if (controller.signal.aborted) return;
    upload.current = null;
    setSubmitting(false);
    if (result.ok) {
      onUploaded((await result.response.json()) as DocumentDetail);
      return;
    }
    const refusal = uploadRefusal(result, file.name);
    // The only field the client can't fully check is the date (the server reads it strictly).
    if (result.code === problemCodes.validation && expiresOn) {
      setErrors({ expiresOn: copy.upload.expiryInvalid });
      setFocusRequest({ field: "expiresOn" });
    } else if (refusal.file) {
      setFile(undefined);
      setErrors({ file: refusal.file });
      setFocusRequest({ field: "file" });
    } else setError(refusal.form);
    if (result.code === problemCodes.versionInFlight && existing)
      void queryClient.invalidateQueries({ queryKey: documentKey(existing.id) });
  };

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
      <form
        noValidate
        className="mt-4 flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault();
          void submit();
        }}
      >
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
              onChange={(event) => {
                setTitle(event.target.value);
                setTyped(true);
                setErrors((current) => ({ ...current, title: undefined }));
              }}
            />
            <SelectField
              ref={typeSelect}
              label={copy.upload.typeLabel}
              placeholder={copy.upload.typePlaceholder}
              options={typeOptions}
              error={errors.type}
              value={type}
              onChange={(event) => {
                setType(event.target.value);
                setErrors((current) => ({ ...current, type: undefined }));
              }}
            />
            <TextField
              ref={expiryInput}
              type="date"
              max="9999-12-31"
              label={copy.upload.expiryLabel}
              hint={copy.upload.expiryHint}
              error={errors.expiresOn}
              value={expiresOn}
              onChange={(event) => {
                setExpiresOn(event.target.value);
                setErrors((current) => ({ ...current, expiresOn: undefined }));
              }}
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
