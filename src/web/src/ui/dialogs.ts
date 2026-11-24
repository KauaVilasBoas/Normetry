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
