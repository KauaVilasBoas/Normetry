import {
  ArrowRight,
  Check,
  CircleAlert,
  Minus,
  Pill,
  Stethoscope,
  Store,
  UtensilsCrossed,
} from "lucide-react";
import { Dialog } from "radix-ui";
import { useId, type ButtonHTMLAttributes, type ComponentProps, type ReactNode } from "react";
import { Link } from "react-router";

const buttonVariants = {
  primary: "bg-mint text-on-mint font-semibold hover:bg-mint-hover",
  secondary: "border border-teal-border text-text-primary font-medium",
};

const buttonBase =
  "inline-flex h-9 items-center justify-center gap-2 rounded-control px-4 text-body";

/** A secondary button's classes, for a link that looks like one ("Back to profiles"). */
export const secondaryButton = `${buttonBase} ${buttonVariants.secondary} hover:bg-elevated`;

/**
 * DESIGN.md buttons: one primary per view; text on mint is always --on-mint. A secondary button
 * hovers to --bg-elevated, or to --bg-inset when it sits on --bg-elevated (`onElevated`).
 */
export function Button({
  variant,
  onElevated = false,
  className = "",
  ...props
}: ComponentProps<"button"> & {
  variant: keyof typeof buttonVariants;
  onElevated?: boolean;
}) {
  const hover = variant === "primary" ? "" : onElevated ? "hover:bg-inset" : "hover:bg-elevated";
  return (
    <button
      type="button"
      className={`${buttonBase} ${buttonVariants[variant]} ${hover} ${className}`}
      {...props}
    />
  );
}

/** 36px icon-only button (give it an aria-label); hovers to --bg-elevated. */
export const iconButton =
  "inline-flex size-9 shrink-0 items-center justify-center rounded-control text-text-secondary hover:bg-elevated";

// Track and arc per surface: an --bg-elevated track vanishes inside an elevated surface, and a
// mint arc on a mint button.
const spinnerSurfaces = {
  card: "border-elevated border-t-mint",
  elevated: "border-inset border-t-mint",
  mint: "border-mint-hover border-t-on-mint",
};

/** Indeterminate ring, 16px; static under reduced motion. `surface`: what it sits on. */
export function Spinner({ surface = "card" }: { surface?: keyof typeof spinnerSurfaces }) {
  return (
    <span
      aria-hidden="true"
      className={`inline-block size-4 shrink-0 rounded-pill border-2 ${spinnerSurfaces[surface]} motion-safe:animate-spin`}
    />
  );
}

/** Loading placeholder bar; pulses gently unless reduced motion is on. */
export function Skeleton({ className }: { className: string }) {
  return (
    <span
      aria-hidden="true"
      className={`block rounded-sm bg-elevated motion-safe:animate-pulse ${className}`}
    />
  );
}

const typeIcons: Record<string, typeof Store | undefined> = {
  Restaurant: UtensilsCrossed,
  Pharmacy: Pill,
  Clinic: Stethoscope,
};

/** Establishment type icon (decorative); unknown types get a generic store. */
export function TypeIcon({ type, className }: { type: string; className: string }) {
  const Icon = typeIcons[type] ?? Store;
  return <Icon aria-hidden="true" className={className} />;
}

/** 28px tile with the establishment type icon. */
export function TypeTile({ type }: { type: string }) {
  return (
    <span className="flex size-7 shrink-0 items-center justify-center rounded-sm border border-border bg-inset">
      <TypeIcon type={type} className="size-4 text-chart-2" />
    </span>
  );
}

/** 32px initials avatar on --teal; decorative, the name is given in text elsewhere. */
export function Avatar({ name }: { name: string }) {
  const initials = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");
  return (
    <span
      aria-hidden="true"
      className="flex size-8 shrink-0 items-center justify-center rounded-pill bg-teal text-small font-semibold"
    >
      {initials}
    </span>
  );
}

/** Empty and error block: optional icon, title and body. `heading`: h2 when no card title is above. */
export function Message({
  icon,
  title,
  body,
  heading: Heading = "h3",
  className = "mt-4 items-start",
}: {
  icon?: ReactNode;
  title: string;
  body: string;
  heading?: "h2" | "h3";
  className?: string;
}) {
  return (
    <div className={`flex flex-col gap-2 ${className}`}>
      {icon}
      <Heading className="text-title font-semibold">{title}</Heading>
      <p className="text-body text-text-secondary">{body}</p>
    </div>
  );
}

/**
 * A load failure inside a card: the message, then Try again (`aria-disabled` with a spinner while
 * it retries, so focus stays on it).
 */
export function ErrorMessage({
  title,
  body,
  retryLabel,
  retrying,
  onRetry,
}: {
  title: string;
  body: string;
  retryLabel: string;
  retrying: boolean;
  onRetry: () => void;
}) {
  return (
    <div role="alert">
      <Message
        icon={<CircleAlert aria-hidden="true" className="size-5 text-text-secondary" />}
        title={title}
        body={body}
      />
      <Button
        variant="secondary"
        className="mt-4"
        aria-disabled={retrying}
        onClick={retrying ? undefined : onRetry}
      >
        {retrying && <Spinner />}
        {retryLabel}
      </Button>
    </div>
  );
}

/** Opens a citation's source; `aria-expanded` while its passage is the one shown. */
export function CitationChip({
  label,
  expanded,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { label: string; expanded: boolean }) {
  return (
    <button
      type="button"
      aria-expanded={expanded}
      // Open: the mint border marks it; the fill alone is 1.7:1 against the bubble.
      className={`inline-flex h-7 max-w-full items-center gap-1.5 rounded-sm border px-2.5 text-small font-medium text-mint focus-visible:-outline-offset-2 ${
        expanded ? "border-mint bg-teal-deep" : "border-teal-border hover:bg-inset"
      }`}
      {...props}
    >
      <span className="truncate">{label}</span>
      <ArrowRight aria-hidden="true" className="size-3.5 shrink-0" />
    </button>
  );
}

/**
 * Modal sheet from the left or right edge over the scrim. Its content names it with a
 * `Dialog.Title` (usually its visible heading, through `asChild`).
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
} & Pick<ComponentProps<typeof Dialog.Content>, "onOpenAutoFocus" | "onCloseAutoFocus">) {
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

/** A plain label ("System", a profile name). Not a status, so never a status color. */
export function Tag({ children }: { children: ReactNode }) {
  return (
    <span className="inline-block h-5 max-w-full shrink-0 truncate rounded-sm bg-inset px-2 text-caption leading-5 font-medium text-text-secondary">
      {children}
    </span>
  );
}

/** Section tabs under the top bar: links, the current one marked with a mint underline. */
export function TabStrip({
  label,
  tabs,
}: {
  label: string;
  tabs: { to: string; label: string; active: boolean }[];
}) {
  return (
    <nav aria-label={label} className="mb-4 flex gap-4 border-b border-border">
      {tabs.map((tab) => (
        <Link
          key={tab.to}
          to={tab.to}
          aria-current={tab.active ? "page" : undefined}
          // Inset ring: the strip is the first thing in the scrolling main, which clips outside it.
          className={`-mb-px flex h-9 items-center rounded-t-sm border-b-2 px-2 text-body font-medium focus-visible:-outline-offset-2 ${
            tab.active
              ? "border-mint text-text-primary"
              : "border-transparent text-text-secondary hover:text-text-primary"
          }`}
        >
          {tab.label}
        </Link>
      ))}
    </nav>
  );
}

/** Native checkbox in a 44px label row; the description is its accessible description. */
export function Checkbox({
  label,
  description,
  ...input
}: Omit<ComponentProps<"input">, "type"> & { label: string; description?: string }) {
  const id = useId();
  return (
    <label className="flex min-h-11 items-start gap-3 rounded-control px-2 py-2 hover:bg-elevated">
      <input
        type="checkbox"
        aria-labelledby={`${id}-label`}
        aria-describedby={description ? `${id}-description` : undefined}
        className="mt-0.5 size-4 shrink-0 accent-mint"
        {...input}
      />
      <CheckboxText id={id} label={label} description={description} />
    </label>
  );
}

/** A checkbox row that cannot change: a check or a dash, and `state` ("Granted:") for screen readers. */
export function ReadOnlyCheckbox({
  checked,
  state,
  label,
  description,
}: {
  checked: boolean;
  state: string;
  label: string;
  description?: string;
}) {
  const id = useId();
  const Icon = checked ? Check : Minus;
  return (
    <div className="flex min-h-11 items-start gap-3 px-2 py-2">
      <Icon
        aria-hidden="true"
        className={`mt-0.5 size-4 shrink-0 ${checked ? "text-text-primary" : "text-text-secondary"}`}
      />
      <CheckboxText
        id={id}
        state={state}
        label={label}
        description={description}
        muted={!checked}
      />
    </div>
  );
}

function CheckboxText({
  id,
  state,
  label,
  description,
  muted = false,
}: {
  id: string;
  state?: string;
  label: string;
  description?: string;
  muted?: boolean;
}) {
  return (
    <span className="min-w-0">
      <span
        id={`${id}-label`}
        className={`block text-body font-medium ${muted ? "text-text-secondary" : ""}`}
      >
        {state && <span className="sr-only">{state} </span>}
        {label}
      </span>
      {description && (
        <span id={`${id}-description`} className="block text-small text-text-secondary">
          {description}
        </span>
      )}
    </span>
  );
}

/** Labelled input with an optional hint and error (no red: an icon and white text). */
export function TextField({
  label,
  hint,
  error,
  ...input
}: ComponentProps<"input"> & { label: string; hint?: string; error?: string }) {
  const id = useId();
  const describedBy = [hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(" ");
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-small text-text-primary">
        {label}
      </label>
      <input
        id={id}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy || undefined}
        className="h-11 w-full rounded-control border border-teal-border bg-inset px-3 text-field sm:text-body"
        {...input}
      />
      {hint && (
        <p id={`${id}-hint`} className="text-small text-text-secondary">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} className="flex items-start gap-1.5 text-small text-text-primary">
          <CircleAlert aria-hidden="true" className="size-4 shrink-0 text-text-secondary" />
          {error}
        </p>
      )}
    </div>
  );
}

/** The dialog surface (DESIGN.md Alert dialog): centered, elevated, max 400px. */
export const dialogPanel =
  "fixed top-1/2 left-1/2 z-40 w-[calc(100%-2rem)] max-w-100 -translate-x-1/2 -translate-y-1/2 rounded-card border border-border bg-elevated p-5 motion-safe:animate-fade-in";

/**
 * A dialog with a form: title, fields, an inline error, then Cancel and the primary submit. It
 * cannot be dismissed while `submitting`. Focus returns to `trigger` unless `onCloseAutoFocus`
 * prevents it.
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
          className={dialogPanel}
        >
          <Dialog.Title className="text-title font-semibold">{title}</Dialog.Title>
          <form
            noValidate
            className="mt-4"
            onSubmit={(event) => {
              event.preventDefault();
              if (!submitting) onSubmit();
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
              <Button type="submit" variant="primary" aria-disabled={submitting}>
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
