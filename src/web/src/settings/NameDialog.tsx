import { useEffect, useRef, useState, type ReactNode } from "react";
import { problemCodes } from "../api/contract";
import { copy, format } from "../copy";
import { FormDialog, TextField } from "../ui/primitives";
import type { Sent } from "./profiles";

/** Profile.NameMaxLength in the core. */
const NAME_MAX_LENGTH = 100;

/**
 * Asks for a profile name (create or rename). `onSubmit` gets the trimmed name; the dialog closes
 * when it succeeds. With `onClosedAfterSave` focus does not go back to the trigger: the caller
 * moves it (after a create, to the new profile).
 */
export function NameDialog({
  trigger,
  title,
  submitLabel,
  submittingLabel,
  initialName = "",
  onSubmit,
  onClosed,
  onClosedAfterSave,
}: {
  trigger: ReactNode;
  title: string;
  submitLabel: string;
  submittingLabel: string;
  initialName?: string;
  onSubmit: (name: string) => Promise<Sent>;
  /** Dismissed (Cancel, Esc, outside): a refusal it showed may now take its trigger away. */
  onClosed?: () => void;
  onClosedAfterSave?: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState(initialName);
  // A new object per refusal, so the same error twice still moves focus to the field.
  const [fieldError, setFieldError] = useState<{ text: string }>();
  const [error, setError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);
  const saved = useRef(false);
  const input = useRef<HTMLInputElement>(null);

  // The field takes focus with its error, so the error is read as its description.
  useEffect(() => {
    if (fieldError) input.current?.focus();
  }, [fieldError]);

  const submit = async () => {
    const trimmed = name.trim();
    setError(undefined);
    if (!trimmed) {
      setFieldError({ text: copy.profiles.nameRequired });
      return;
    }
    setSubmitting(true);
    const result = await onSubmit(trimmed);
    setSubmitting(false);
    if (result.ok) {
      saved.current = true;
      setOpen(false);
    } else if (result.code === problemCodes.nameTaken) {
      setFieldError({ text: format(copy.profiles.nameTaken, { name: trimmed }) });
    } else {
      setError(result.status === 403 ? copy.settings.denied : copy.profiles.nameError);
    }
  };

  return (
    <FormDialog
      trigger={trigger}
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) {
          onClosed?.();
          return;
        }
        setName(initialName);
        setFieldError(undefined);
        setError(undefined);
      }}
      title={title}
      submitLabel={submitting ? submittingLabel : submitLabel}
      cancelLabel={copy.settings.cancel}
      submitting={submitting}
      error={error}
      onSubmit={() => void submit()}
      onCloseAutoFocus={(event) => {
        if (saved.current && onClosedAfterSave) {
          event.preventDefault();
          onClosedAfterSave();
        }
        saved.current = false;
      }}
    >
      <TextField
        ref={input}
        label={copy.profiles.nameLabel}
        hint={copy.profiles.nameHint}
        error={fieldError?.text}
        value={name}
        maxLength={NAME_MAX_LENGTH}
        autoComplete="off"
        onChange={(event) => {
          setName(event.target.value);
          setFieldError(undefined);
        }}
      />
    </FormDialog>
  );
}
