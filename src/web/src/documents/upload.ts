import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import type { RequestOptions } from "../api/client";
import { problemCodes } from "../api/contract";
import {
  documentKey,
  documentsKey,
  documentsPath,
  documentVersionsPath,
  type DocumentDetail,
} from "../api/documents";
import type { Establishment } from "../api/queries";
import type { Sent } from "../api/send";
import { copy, format } from "../copy";
import {
  expiryProblem,
  fileProblem,
  titleFromFile,
  uploadRefusal,
  withContentType,
} from "./documents";

type Field = "file" | "title" | "type" | "expiresOn";
type FieldErrors = Partial<Record<Field, string>>;

const fieldOrder = ["file", "title", "type", "expiresOn"] as const;

/**
 * The upload form (spec §2, §5): the chosen file (or the one dropped on the list, in `files`), the
 * title it suggests until one is typed, type and expiry for a new document, and their errors.
 * Submit checks the fields, focusing the first invalid one once its error shows, then posts;
 * unmounting (the dialog closing) aborts a running upload. A refusal shows on the field it is
 * about, else on the form.
 */
export function useUploadForm({
  files,
  establishment,
  existing,
  send,
  onUploaded,
}: {
  files: File[];
  establishment: Establishment;
  /** The document a new version is for; a new document when undefined. */
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
  const [errors, setErrors] = useState<FieldErrors>(dropped ? { file: dropped } : {});
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

  const clearError = (field: Field) => setErrors((current) => ({ ...current, [field]: undefined }));

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
    clearError("file");
    if (!typed) setTitle(titleFromFile(picked.name));
    setChosen(format(copy.upload.chosen, { file: picked.name }));
  };

  const changeTitle = (event: ChangeEvent<HTMLInputElement>) => {
    setTitle(event.target.value);
    setTyped(true);
    clearError("title");
  };

  const changeType = (event: ChangeEvent<HTMLSelectElement>) => {
    setType(event.target.value);
    clearError("type");
  };

  const changeExpiresOn = (event: ChangeEvent<HTMLInputElement>) => {
    setExpiresOn(event.target.value);
    clearError("expiresOn");
  };

  const fieldErrors = (): FieldErrors => ({
    file: file ? undefined : (errors.file ?? copy.upload.fileRequired),
    title: existing || title.trim() ? undefined : copy.upload.titleRequired,
    type: existing || type ? undefined : copy.upload.typeRequired,
    expiresOn: existing ? undefined : expiryProblem(expiryInput.current),
  });

  const formData = (picked: File) => {
    const form = new FormData();
    form.append("file", withContentType(picked));
    if (!existing) {
      form.append("title", title.trim());
      form.append("type", type);
      if (expiresOn) form.append("expiresOn", expiresOn);
    }
    return form;
  };

  const showRefusal = (result: Sent & { ok: false }, picked: File) => {
    const refusal = uploadRefusal(result, picked.name);
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

  const post = async (picked: File) => {
    const form = formData(picked);
    const controller = new AbortController();
    upload.current = controller;
    setSubmitting(true);
    const result = await send(
      existing ? documentVersionsPath(existing.id) : documentsPath(establishment.id),
      { method: "POST", form, signal: controller.signal },
    );
    if (controller.signal.aborted) return;
    upload.current = null;
    setSubmitting(false);
    if (result.ok) onUploaded((await result.response.json()) as DocumentDetail);
    else showRefusal(result, picked);
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (submitting) return;
    setError(undefined);
    const found = fieldErrors();
    setErrors(found);
    const first = fieldOrder.find((field) => found[field]);
    if (first || !file) setFocusRequest({ field: first ?? "file" });
    else void post(file);
  };

  return {
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
  };
}
