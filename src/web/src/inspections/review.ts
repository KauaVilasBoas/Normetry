import { useEffect, useRef, useState, type ChangeEvent } from "react";
import type { RequestOptions } from "../api/client";
import { reviewStatuses } from "../api/contract";
import type { Finding } from "../api/inspections";
import { copy } from "../copy";
import { useFormDialog, type Submitted } from "../ui/dialogs";
import { useFieldErrorFocus } from "../ui/focus";
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

/** Sends a review change to `path` (one of `findingPaths`); resolves to its refusal, if any. */
export type Review = ReturnType<typeof useReview>;

/** The footer's Accept: sends it, then says it or shows why it failed. */
export function useAccept(path: string, review: Review, announce: (text: string) => void) {
  const [accepting, setAccepting] = useState(false);
  const [error, setError] = useState<string>();

  const accept = async () => {
    setAccepting(true);
    setError(undefined);
    const refusal = await review(path, { method: "POST" });
    setAccepting(false);
    if (refusal) setError(reviewError(refusal));
    else announce(copy.findings.accepted);
  };

  return { accepting, error, accept };
}

/** A review sent from its dialog: a refusal shows there; saved, `message` is said once it closes. */
async function sendReview(
  review: Review,
  path: string,
  options: RequestOptions,
  message: string,
): Promise<Submitted<string>> {
  const refusal = await review(path, options);
  return refusal ? { error: reviewError(refusal) } : { saved: message };
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
  const [note, setNote] = useState("");
  const field = useRef<HTMLTextAreaElement>(null);
  const { fieldError, setFieldError } = useFieldErrorFocus(field);
  const { submit: submitDialog, ...dialog } = useFormDialog<string>({
    reset: () => {
      setNote(finding.reviewStatus === reviewStatuses.rejected ? (finding.reviewNote ?? "") : "");
      setFieldError();
    },
    onSaved: (message) => announce(message),
  });

  const changeNote = (event: ChangeEvent<HTMLTextAreaElement>) => {
    setNote(event.target.value);
    setFieldError();
  };

  const submit = () =>
    submitDialog(() => {
      const trimmed = note.trim();
      if (trimmed)
        return sendReview(
          review,
          path,
          { method: "POST", json: { note: trimmed } },
          copy.findings.rejected,
        );
      setFieldError(copy.findings.reasonRequired);
      return undefined;
    });

  return { ...dialog, note, changeNote, field, fieldError, submit };
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
  const [draft, setDraft] = useState(() => draftOf(finding));
  const [errors, setErrors] = useState<EditErrors>({});
  const [focusRequest, setFocusRequest] = useState<{ field: keyof EditDraft }>();
  const rationaleField = useRef<HTMLTextAreaElement>(null);
  const actionTextField = useRef<HTMLTextAreaElement>(null);
  const dueOnField = useRef<HTMLInputElement>(null);
  const changes = editChanges(finding, draft);
  const { submit: submitDialog, ...dialog } = useFormDialog<string>({
    reset: () => {
      setDraft(draftOf(finding));
      setErrors({});
    },
    onSaved: (message) => announce(message),
  });

  useEffect(() => {
    if (!focusRequest) return;
    const fields: Partial<Record<keyof EditDraft, HTMLElement | null>> = {
      rationale: rationaleField.current,
      actionText: actionTextField.current,
      dueOn: dueOnField.current,
    };
    fields[focusRequest.field]?.focus();
  }, [focusRequest]);

  const change = (key: keyof EditDraft) => (event: { target: { value: string } }) => {
    setDraft({ ...draft, [key]: event.target.value });
    setErrors({ ...errors, [key]: undefined });
  };

  const submit = () =>
    submitDialog(() => {
      const found = editErrors(finding, draft);
      const first = editFields.find((key) => found[key]);
      if (!first)
        return sendReview(review, path, { method: "PATCH", json: changes }, copy.findings.edited);
      setErrors(found);
      setFocusRequest({ field: first });
      return undefined;
    });

  return {
    ...dialog,
    draft,
    change,
    errors,
    rationaleField,
    actionTextField,
    dueOnField,
    ready: Object.keys(changes).length > 0,
    submit,
  };
}
