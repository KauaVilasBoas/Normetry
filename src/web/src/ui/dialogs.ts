import { useRef, useState } from "react";

/**
 * A confirmation's state (`ConfirmDialog`). `confirm` holds `confirming` while `onConfirm` runs: a
 * refusal it resolves to shows in the dialog, which stays open; undefined means done, and the
 * caller takes the dialog away (it stays busy until then). It cannot be dismissed while
 * confirming; closing clears the refusal and runs `onClosed`. `afterClose` puts focus back on the
 * trigger after Cancel; after a confirm the trigger is gone, and the next view takes focus.
 */
export function useConfirm(onConfirm: () => Promise<string | undefined>, onClosed: () => void) {
  const [open, setOpen] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const triggerRef = useRef<HTMLButtonElement>(null);

  const openChange = (next: boolean) => {
    if (confirming) return;
    setOpen(next);
    setRefusal(undefined);
    if (!next) onClosed();
  };

  const confirm = async () => {
    setConfirming(true);
    setRefusal(undefined);
    const refused = await onConfirm();
    if (refused === undefined) return;
    setConfirming(false);
    setRefusal(refused);
  };

  const afterClose = (event: Event) => {
    event.preventDefault();
    if (triggerRef.current?.isConnected) triggerRef.current.focus();
  };

  return { open, openChange, confirming, refusal, confirm, triggerRef, afterClose };
}

/**
 * What a form dialog's request came to: saved (the dialog closes), a refusal to show in the
 * dialog, or undefined when a field shows why.
 */
export type Submitted<T> = { saved: T } | { error: string } | undefined;

/**
 * A form dialog's state (`FormDialog`). Opening clears its error and runs `reset`, so the form
 * starts over; closing runs `onClosed`. `submit(request)` clears the error and runs `request`,
 * which returns undefined when a field shows why the form can't be sent, else its request: the
 * dialog is `submitting` until it answers. Saved closes the dialog, and `afterClose` (its
 * `onCloseAutoFocus`) hands what was saved to `onSaved` once it has closed, when the page is heard
 * again.
 */
export function useFormDialog<T>({
  reset,
  onClosed,
  onSaved,
}: {
  reset: () => void;
  onClosed?: () => void;
  onSaved: (saved: T, event: Event) => void;
}) {
  const [open, setOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string>();
  const saved = useRef<{ value: T }>(undefined);

  const openChange = (next: boolean) => {
    setOpen(next);
    if (!next) {
      onClosed?.();
      return;
    }
    reset();
    setError(undefined);
  };

  const submit = async (request: () => Promise<Submitted<T>> | undefined) => {
    setError(undefined);
    const sent = request();
    if (!sent) return;
    setSubmitting(true);
    const outcome = await sent;
    setSubmitting(false);
    if (!outcome) return;
    if ("error" in outcome) {
      setError(outcome.error);
      return;
    }
    saved.current = { value: outcome.saved };
    setOpen(false);
  };

  const afterClose = (event: Event) => {
    if (saved.current) onSaved(saved.current.value, event);
    saved.current = undefined;
  };

  return { open, openChange, submitting, error, submit, afterClose };
}
