import { useEffect, useRef, useState, type ChangeEvent } from "react";
import { reviewStatuses } from "../api/contract";
import type { Finding } from "../api/queries";
import { copy } from "../copy";
import {
  draftOf,
  editChanges,
  editErrors,
  editFields,
  reviewError,
  type EditDraft,
  type EditErrors,
  type useReview,
} from "./findings";

/** Sends a review change to the finding at `path`; resolves to its refusal, if any. */
export type Review = ReturnType<typeof useReview>;

/** The footer's Accept: sends it, then says it or shows why it failed. */
export function useAccept(path: string, review: Review, announce: (text: string) => void) {
  const [accepting, setAccepting] = useState(false);
  const [error, setError] = useState<string>();

  const accept = async () => {
    setAccepting(true);
    setError(undefined);
    const refusal = await review(`${path}/accept`, { method: "POST" });
    setAccepting(false);
    if (refusal) setError(reviewError(refusal));
    else announce(copy.findings.accepted);
  };

  return { accepting, error, accept };
}

/**
 * The Reject dialog's form: a reason, required, prefilled on opening with the current one when
 * the finding is already rejected. A missing reason moves focus to the field; a refusal shows in
 * the dialog. Saving closes it, and `afterClose` announces it, when the sheet is heard again.
 */
export function useRejectForm(
  finding: Finding,
  path: string,
  review: Review,
  announce: (text: string) => void,
) {
  const [open, setOpen] = useState(false);
  const [note, setNote] = useState("");
  // A new object per refusal, so the same error twice still moves focus to the field.
  const [fieldError, setFieldError] = useState<{ text: string }>();
  const [error, setError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);
  const saved = useRef(false);
  const field = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (fieldError) field.current?.focus();
  }, [fieldError]);

  const openChange = (next: boolean) => {
    setOpen(next);
    if (!next) return;
    setNote(finding.reviewStatus === reviewStatuses.rejected ? (finding.reviewNote ?? "") : "");
    setFieldError(undefined);
    setError(undefined);
  };

  const changeNote = (event: ChangeEvent<HTMLTextAreaElement>) => {
    setNote(event.target.value);
    setFieldError(undefined);
  };

  const submit = async () => {
    const trimmed = note.trim();
    setError(undefined);
    if (!trimmed) {
      setFieldError({ text: copy.findings.reasonRequired });
      return;
    }
    setSubmitting(true);
    const refusal = await review(`${path}/reject`, { method: "POST", json: { note: trimmed } });
    setSubmitting(false);
    if (refusal) {
      setError(reviewError(refusal));
      return;
    }
    saved.current = true;
    setOpen(false);
  };

  const afterClose = () => {
    if (saved.current) announce(copy.findings.rejected);
    saved.current = false;
  };

  return {
    open,
    openChange,
    note,
    changeNote,
    field,
    fieldError: fieldError?.text,
    error,
    submitting,
    submit,
    afterClose,
  };
}

/**
 * The Edit dialog's form: the current values on opening, any of which can change. Only changed
 * fields are sent, and `ready` waits for one. On errors focus moves to the first invalid field;
 * a refusal shows in the dialog. Saving closes it, and `afterClose` announces it.
 */
export function useEditForm(
  finding: Finding,
  path: string,
  review: Review,
  announce: (text: string) => void,
) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(() => draftOf(finding));
  const [errors, setErrors] = useState<EditErrors>({});
  const [focusRequest, setFocusRequest] = useState<{ field: keyof EditDraft }>();
  const [error, setError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);
  const saved = useRef(false);
  const rationaleField = useRef<HTMLTextAreaElement>(null);
  const actionTextField = useRef<HTMLTextAreaElement>(null);
  const dueOnField = useRef<HTMLInputElement>(null);
  const changes = editChanges(finding, draft);

  useEffect(() => {
    if (!focusRequest) return;
    const fields: Partial<Record<keyof EditDraft, HTMLElement | null>> = {
      rationale: rationaleField.current,
      actionText: actionTextField.current,
      dueOn: dueOnField.current,
    };
    fields[focusRequest.field]?.focus();
  }, [focusRequest]);

  const openChange = (next: boolean) => {
    setOpen(next);
    if (!next) return;
    setDraft(draftOf(finding));
    setErrors({});
    setError(undefined);
  };

  const change = (key: keyof EditDraft) => (event: { target: { value: string } }) => {
    setDraft({ ...draft, [key]: event.target.value });
    setErrors({ ...errors, [key]: undefined });
  };

  const submit = async () => {
    setError(undefined);
    const found = editErrors(finding, draft);
    const first = editFields.find((key) => found[key]);
    if (first) {
      setErrors(found);
      setFocusRequest({ field: first });
      return;
    }
    setSubmitting(true);
    const refusal = await review(path, { method: "PATCH", json: changes });
    setSubmitting(false);
    if (refusal) {
      setError(reviewError(refusal));
      return;
    }
    saved.current = true;
    setOpen(false);
  };

  const afterClose = () => {
    if (saved.current) announce(copy.findings.edited);
    saved.current = false;
  };

  return {
    open,
    openChange,
    draft,
    change,
    errors,
    rationaleField,
    actionTextField,
    dueOnField,
    error,
    submitting,
    ready: Object.keys(changes).length > 0,
    submit,
    afterClose,
  };
}
