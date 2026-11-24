import type { ReactNode } from "react";
import type { Sent } from "../api/send";
import { copy } from "../copy";
import { FormDialog, TextField } from "../ui/primitives";
import { useNameForm } from "./editor";

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
  const {
    open,
    openChange,
    name,
    changeName,
    input,
    fieldError,
    error,
    submitting,
    submit,
    afterClose,
  } = useNameForm({ initialName, onSubmit, onClosed, onClosedAfterSave });

  return (
    <FormDialog
      trigger={trigger}
      open={open}
      onOpenChange={openChange}
      title={title}
      submitLabel={submitting ? submittingLabel : submitLabel}
      cancelLabel={copy.settings.cancel}
      submitting={submitting}
      error={error}
      onSubmit={() => void submit()}
      onCloseAutoFocus={afterClose}
    >
      <TextField
        ref={input}
        label={copy.profiles.nameLabel}
        hint={copy.profiles.nameHint}
        error={fieldError}
        value={name}
        maxLength={NAME_MAX_LENGTH}
        autoComplete="off"
        onChange={changeName}
      />
    </FormDialog>
  );
}
