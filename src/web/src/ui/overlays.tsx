import { Dialog } from "radix-ui";
import type { ComponentProps, ReactNode } from "react";
import { Button } from "./buttons";
import { Spinner } from "./feedback";

/**
 * Modal sheet from the left or right edge over the scrim. Its content names it with a
 * `Dialog.Title` (usually its visible heading, through `asChild`), or `aria-labelledby` names a
 * heading that keeps its own id.
 */
export function Sheet({
  side,
  open,
  onOpenChange,
  className = "",
  children,
  ...content
}: {
  side: "left" | "right";
  open: boolean;
  onOpenChange: (open: boolean) => void;
  className?: string;
  children: ReactNode;
} & Pick<
  ComponentProps<typeof Dialog.Content>,
  "onOpenAutoFocus" | "onCloseAutoFocus" | "aria-labelledby"
>) {
  const edge =
    side === "left"
      ? "left-0 border-r motion-safe:animate-drawer-in"
      : "right-0 border-l motion-safe:animate-sheet-in";
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-scrim" />
        <Dialog.Content
          aria-describedby={undefined}
          className={`fixed inset-y-0 z-40 flex flex-col border-border bg-card ${edge} ${className}`}
          {...content}
        >
          {children}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

const dialogSurface =
  "fixed top-1/2 left-1/2 z-40 w-[calc(100%-2rem)] -translate-x-1/2 -translate-y-1/2 rounded-card border border-border bg-elevated p-5 motion-safe:animate-fade-in";

/** The dialog surface (DESIGN.md Alert dialog): centered, elevated, max 400px. */
export const dialogPanel = `${dialogSurface} max-w-100`;

/** A dialog that holds an upload: 480px, scrolling inside itself on a short screen. */
export const uploadDialogPanel = `${dialogSurface} max-h-[calc(100dvh-2rem)] max-w-120 overflow-y-auto`;

/**
 * A dialog with a form: title, fields, an inline error, then Cancel and the primary submit. It
 * cannot be dismissed while `submitting`. Focus returns to `trigger` unless `onCloseAutoFocus`
 * prevents it. `wide`: 480px and scrolling inside itself, for more than three fields. Until
 * `ready` (nothing changed yet), the submit stays `aria-disabled` at 50%.
 */
export function FormDialog({
  trigger,
  open,
  onOpenChange,
  title,
  submitLabel,
  cancelLabel,
  submitting,
  error,
  onSubmit,
  onCloseAutoFocus,
  wide = false,
  ready = true,
  children,
}: {
  trigger: ReactNode;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  submitLabel: string;
  cancelLabel: string;
  submitting: boolean;
  error: string | undefined;
  onSubmit: () => void;
  onCloseAutoFocus?: (event: Event) => void;
  wide?: boolean;
  ready?: boolean;
  children: ReactNode;
}) {
  return (
    <Dialog.Root
      open={open}
      onOpenChange={(next) => {
        if (!submitting) onOpenChange(next);
      }}
    >
      <Dialog.Trigger asChild>{trigger}</Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-scrim" />
        <Dialog.Content
          aria-describedby={undefined}
          onCloseAutoFocus={onCloseAutoFocus}
          className={wide ? uploadDialogPanel : dialogPanel}
        >
          <Dialog.Title className="text-title font-semibold">{title}</Dialog.Title>
          <form
            noValidate
            className="mt-4"
            onSubmit={(event) => {
              event.preventDefault();
              if (!submitting && ready) onSubmit();
            }}
          >
            {children}
            {error && (
              <p role="alert" className="mt-3 text-body">
                {error}
              </p>
            )}
            <div className="mt-5 flex justify-end gap-2">
              <Dialog.Close asChild>
                <Button variant="secondary" onElevated>
                  {cancelLabel}
                </Button>
              </Dialog.Close>
              <Button
                type="submit"
                variant="primary"
                aria-disabled={submitting || !ready}
                className={`aria-disabled:hover:bg-mint ${ready ? "" : "opacity-50"}`}
              >
                {submitting && <Spinner surface="mint" />}
                {submitLabel}
              </Button>
            </div>
          </form>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
