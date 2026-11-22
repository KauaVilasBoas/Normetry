import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState, type ChangeEvent } from "react";
import { ApiError } from "../api/client";
import { problemCodes } from "../api/contract";
import {
  documentKey,
  documentsKey,
  useDocument,
  type DocumentDetail,
  type DocumentSummary,
} from "../api/documents";
import { queryFailed } from "../api/queries";
import { useSend } from "../api/send";
import { copy } from "../copy";
import { focusById } from "../ui/focus";
import { cachedTitle, expiryProblem, inFlight } from "./documents";
import { DETAIL_TITLE_ID } from "./parts";

/**
 * The document the detail view shows: its title (the list's row while it loads), whether it was
 * not found or failed to load, Try again, and the cache after a delete or an upload here.
 */
export function useDocumentDetail({
  id,
  establishmentId,
  onDeleted,
  announce,
}: {
  id: string;
  establishmentId: string;
  onDeleted: (title: string) => void;
  announce: (text: string) => void;
}) {
  const queryClient = useQueryClient();
  const query = useDocument(id, true);
  const status = query.error instanceof ApiError ? query.error.status : 0;
  const notFound = queryFailed(query) && (status === 404 || status === 400);
  const failed = queryFailed(query) && !notFound;
  const detail = query.data;
  const retried = useRef(false);

  // Try again: the error card (and its button) goes once the document loads, so focus moves to
  // the title, after the commit that shows it.
  useEffect(() => {
    if (!retried.current || !detail) return;
    retried.current = false;
    focusById(DETAIL_TITLE_ID);
  }, [detail]);

  const retry = () => {
    retried.current = true;
    void query.refetch();
  };

  const deleted = (title: string) => {
    queryClient.removeQueries({ queryKey: documentKey(id) });
    queryClient.setQueryData<DocumentSummary[]>(documentsKey(establishmentId), (list) =>
      list?.filter((row) => row.id !== id),
    );
    void queryClient.invalidateQueries({ queryKey: documentsKey(establishmentId) });
    onDeleted(title);
  };

  const uploaded = (version: DocumentDetail) => {
    // Set before the dialog closes, so focus comes back to a button already disabled.
    queryClient.setQueryData(documentKey(id), version);
    void queryClient.invalidateQueries({ queryKey: documentsKey(establishmentId) });
    announce(copy.documents.versionUploaded);
  };

  return {
    detail,
    title: detail?.title ?? cachedTitle(queryClient, establishmentId, id),
    notFound,
    failed,
    retrying: failed && query.isFetching,
    retry,
    /** The newest version is Waiting or Processing: Upload new version waits for it. */
    newestInFlight: detail?.versions[0] !== undefined && inFlight(detail.versions[0]),
    deleted,
    uploaded,
  };
}

/**
 * The expiry dialog's form (`documents.upload`): the field starts at the current date on opening;
 * Save sends it, and an empty field clears the date (`removeDate` empties it). A validation
 * refusal shows on the field, which takes focus; any other in the dialog. A 403 refetches /me
 * once the dialog closes, so its message stays readable until then. Saving closes the dialog, and
 * `afterClose` announces it, when the page is heard again.
 */
export function useExpiryForm(detail: DocumentDetail, announce: (text: string) => void) {
  const queryClient = useQueryClient();
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
      if (result.code === problemCodes.validation)
        setFieldError({ text: copy.upload.expiryInvalid });
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

  const openChange = (next: boolean) => {
    setOpen(next);
    if (!next) {
      closed();
      return;
    }
    setValue(current);
    setFieldError(undefined);
    setError(undefined);
  };

  const changeValue = (event: ChangeEvent<HTMLInputElement>) => {
    setValue(event.target.value);
    setFieldError(undefined);
  };

  const submit = () => {
    const problem = expiryProblem(input.current);
    if (problem) setFieldError({ text: problem });
    else void save(value || null);
  };

  const removeDate = () => {
    // Focus moves first: the button goes once the field is empty.
    input.current?.focus();
    setValue("");
    setFieldError(undefined);
  };

  const afterClose = () => {
    if (saved.current) announce(saved.current);
    saved.current = undefined;
  };

  return {
    open,
    openChange,
    value,
    changeValue,
    input,
    fieldError: fieldError?.text,
    error,
    submitting,
    ready: value !== current,
    submit,
    removeDate,
    afterClose,
  };
}
