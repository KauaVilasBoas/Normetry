import {
  ArrowRight,
  Check,
  ChevronDown,
  Circle,
  CircleAlert,
  CircleCheck,
  CircleX,
  Clock,
  Diamond,
  FileText,
  Minus,
  Pencil,
  Pill,
  Stethoscope,
  Store,
  Upload,
  UtensilsCrossed,
  WifiOff,
  type LucideIcon,
} from "lucide-react";
import { Dialog } from "radix-ui";
import {
  useId,
  useRef,
  useState,
  type ButtonHTMLAttributes,
  type ComponentProps,
  type DragEvent,
  type Ref,
  type ReactNode,
} from "react";
import { Link } from "react-router";
import type { Live } from "../api/live";
import { quoteFound, type Citation } from "../api/queries";
import { copy, locale } from "../copy";
import { pillVariant, type PillVariant } from "./statuses";

const buttonVariants = {
  primary: "bg-mint text-on-mint font-semibold hover:bg-mint-hover",
  secondary: "border border-teal-border text-text-primary font-medium",
};

const buttonBase =
  "inline-flex h-9 items-center justify-center gap-2 rounded-control px-4 text-body";

/** A secondary button's classes, for a link that looks like one ("Back to profiles"). */
export const secondaryButton = `${buttonBase} ${buttonVariants.secondary} hover:bg-elevated`;

/**
 * DESIGN.md Text action: a secondary inline action shown as a link ("Edit expiry date", "Open
 * document"), never the view's primary. 24px tall, a reachable target beside text (WCAG 2.5.8).
 */
export const textAction =
  "inline-flex h-6 items-center rounded-sm text-small font-medium text-mint hover:underline";

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

/**
 * Indeterminate ring, 16px (12px when `small`, inside a pill); static under reduced motion.
 * `surface`: what it sits on.
 */
export function Spinner({
  surface = "card",
  small = false,
}: {
  surface?: keyof typeof spinnerSurfaces;
  small?: boolean;
}) {
  return (
    <span
      aria-hidden="true"
      className={`inline-block ${small ? "size-3" : "size-4"} shrink-0 rounded-pill border-2 ${spinnerSurfaces[surface]} motion-safe:animate-spin`}
    />
  );
}

// DESIGN.md Status, Ingestion and Review status pills. Process and review states (waiting to
// rejected) use no green or red; only compliance does. Glyphs are 12px; the filled diamond is
// 10px and the dot 8px, which balances the outline glyphs.
const pills: Record<PillVariant, { className: string; glyph: ReactNode }> = {
  waiting: {
    className: "border-neutral bg-neutral/10 text-text-secondary",
    glyph: <Clock aria-hidden="true" className="size-3 shrink-0" />,
  },
  processing: { className: "border-mint bg-mint/10 text-mint", glyph: <Spinner small /> },
  done: {
    className: "border-teal-border text-text-primary",
    glyph: <CircleCheck aria-hidden="true" className="size-3 shrink-0 text-mint" />,
  },
  attention: {
    className: "border-warning bg-warning/10 text-warning",
    glyph: <Diamond aria-hidden="true" className="size-2.5 shrink-0 fill-current" />,
  },
  compliant: {
    className: "border-success bg-success/10 text-success",
    glyph: <Diamond aria-hidden="true" className="size-2.5 shrink-0 fill-current" />,
  },
  // White text: --danger on its own 10% fill is only 4.3:1.
  nonCompliant: {
    className: "border-danger bg-danger/10 text-text-primary",
    glyph: <Circle aria-hidden="true" className="size-2 shrink-0 fill-current text-danger" />,
  },
  edited: {
    className: "border-teal-border text-text-primary",
    glyph: <Pencil aria-hidden="true" className="size-3 shrink-0 text-mint" />,
  },
  rejected: {
    className: "border-teal-border text-text-secondary",
    glyph: <CircleX aria-hidden="true" className="size-3 shrink-0" />,
  },
};

/** A status pill: glyph and label, never color alone. `pillVariant()` maps an API status. */
export function StatusPill({ variant, label }: { variant: PillVariant; label: string }) {
  const pill = pills[variant];
  return (
    <span
      className={`inline-flex h-5 items-center gap-1 rounded-pill border px-2 text-caption font-medium whitespace-nowrap ${pill.className}`}
    >
      {pill.glyph}
      {label}
    </span>
  );
}

/** A document version's ingestion status. An unknown status looks like Uploaded. */
export function IngestionPill({ status, label }: { status: string; label: string }) {
  return <StatusPill variant={pillVariant("ingestion", status)} label={label} />;
}

/** A note inside a card (DESIGN.md Inline notice): an icon, a title, its lines, an optional action. */
export function Notice({
  icon,
  title,
  action,
  children,
  className = "",
  ...props
}: Omit<ComponentProps<"div">, "title"> & { icon: ReactNode; title: string; action?: ReactNode }) {
  return (
    <div
      className={`flex flex-wrap items-center gap-x-3 gap-y-2 rounded-sm bg-inset p-3 ${className}`}
      {...props}
    >
      <div className="flex min-w-0 flex-1 gap-2">
        <span className="mt-0.5 flex shrink-0 text-text-secondary">{icon}</span>
        <div className="min-w-0">
          <p className="text-body font-semibold">{title}</p>
          {children && <div className="text-small text-text-secondary">{children}</div>}
        </div>
      </div>
      {action}
    </div>
  );
}

/**
 * Under a card's header: a status region, always there so screen readers hear what arrives in it,
 * that holds the notice while live updates are paused or stopped. `labels`: the screen's copy.
 * `className` places the notice (inside a card by default).
 */
export function ConnectionNotice({
  live,
  labels,
  className = "mx-5 mb-4",
}: {
  live: Live;
  labels: { reconnecting: string; stopped: string; reconnect: string };
  className?: string;
}) {
  const reconnecting = live.state === "reconnecting";
  const retrying = live.state === "retrying";
  return (
    <div role="status">
      {live.state !== "live" && (
        <Notice
          className={className}
          icon={reconnecting ? <Spinner /> : <WifiOff aria-hidden="true" className="size-4" />}
          title={reconnecting ? labels.reconnecting : labels.stopped}
          action={
            !reconnecting && (
              // aria-disabled, not disabled, while it reconnects: focus stays on the button.
              <Button
                variant="secondary"
                aria-disabled={retrying}
                onClick={retrying ? undefined : live.reconnect}
              >
                {retrying && <Spinner />}
                {labels.reconnect}
              </Button>
            )
          }
        />
      )}
    </div>
  );
}

/** A table header cell (DESIGN.md Table, on the inset header row). */
export const th = "h-9 px-3 text-left text-caption font-medium text-text-muted";

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

/**
 * Opens a citation's source; `aria-expanded` while its passage is the one shown. `icon` leads the
 * label (a document citation's `FileText`); `detail` follows it and keeps up to half the chip, so a
 * long title truncates before the section does.
 */
export function CitationChip({
  label,
  detail,
  expanded,
  icon: Icon,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  label: string;
  detail?: string;
  expanded: boolean;
  icon?: LucideIcon;
}) {
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
      {Icon && <Icon aria-hidden="true" className="size-3.5 shrink-0" />}
      <span className="min-w-0 truncate">{label}</span>
      {detail && <span className="max-w-1/2 shrink-0 truncate">{detail}</span>}
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

/**
 * Native checkbox in a 44px label row; the description is its accessible description, followed
 * by whatever `aria-describedby` adds (a badge outside the row).
 */
export function Checkbox({
  label,
  description,
  "aria-describedby": describedBy,
  ...input
}: Omit<ComponentProps<"input">, "type"> & { label: string; description?: string }) {
  const id = useId();
  const describedByIds = [description && `${id}-description`, describedBy]
    .filter(Boolean)
    .join(" ");
  return (
    <label className="flex min-h-11 items-start gap-3 rounded-control px-2 py-2 hover:bg-elevated">
      <input
        type="checkbox"
        aria-labelledby={`${id}-label`}
        aria-describedby={describedByIds || undefined}
        className="mt-0.5 size-4 shrink-0 accent-mint"
        {...input}
      />
      <CheckboxText id={id} label={label} description={description} />
    </label>
  );
}

/**
 * A checkbox row that cannot change: a check or a dash, and `state` ("Granted:") for screen
 * readers. An unchecked row is dimmed unless `dimUnchecked` is false (an open action matters
 * as much as a done one).
 */
export function ReadOnlyCheckbox({
  checked,
  state,
  label,
  description,
  dimUnchecked = true,
}: {
  checked: boolean;
  state: string;
  label: string;
  description?: string;
  dimUnchecked?: boolean;
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
        muted={dimUnchecked && !checked}
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

const fieldControl =
  "h-11 w-full rounded-control border border-teal-border bg-inset px-3 text-field sm:text-body";

/** A field's `aria-describedby` and `aria-invalid`, for its hint and error. */
function fieldAria(id: string, hint: string | undefined, error: string | undefined) {
  const describedBy = [hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(" ");
  return {
    "aria-invalid": error ? (true as const) : undefined,
    "aria-describedby": describedBy || undefined,
  };
}

/** A field's hint, then its error (no red: an icon and white text). */
function FieldNotes({ id, hint, error }: { id: string; hint?: string; error?: string }) {
  return (
    <>
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
    </>
  );
}

/** Labelled input with an optional hint and error. */
export function TextField({
  label,
  hint,
  error,
  ...input
}: ComponentProps<"input"> & { label: string; hint?: string; error?: string }) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-small text-text-primary">
        {label}
      </label>
      <input id={id} {...fieldAria(id, hint, error)} className={fieldControl} {...input} />
      <FieldNotes id={id} hint={hint} error={error} />
    </div>
  );
}

/** The text field with a `<textarea>`: at least 88px tall, resizing vertically. */
export function TextAreaField({
  label,
  hint,
  error,
  ...textarea
}: ComponentProps<"textarea"> & { label: string; hint?: string; error?: string }) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-small text-text-primary">
        {label}
      </label>
      <textarea
        id={id}
        {...fieldAria(id, hint, error)}
        className="min-h-22 w-full resize-y rounded-control border border-teal-border bg-inset px-3 py-2.5 text-field sm:text-body"
        {...textarea}
      />
      <FieldNotes id={id} hint={hint} error={error} />
    </div>
  );
}

/**
 * A compact filter in a card header (DESIGN.md Dropdown / select): a native select with no
 * visible label, so `label` names it.
 */
export function FilterSelect({
  label,
  options,
  ...select
}: Omit<ComponentProps<"select">, "children"> & {
  label: string;
  options: { value: string; label: string }[];
}) {
  return (
    <div className="relative w-full sm:w-auto">
      <select
        aria-label={label}
        className="h-9 w-full appearance-none rounded-sm border border-teal-border bg-inset pr-8 pl-2.5 text-small font-medium"
        {...select}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      <ChevronDown
        aria-hidden="true"
        className="pointer-events-none absolute top-1/2 right-2.5 size-4 -translate-y-1/2 text-text-secondary"
      />
    </div>
  );
}

/**
 * A native select as a form field (DESIGN.md Form select): the text field's look with a chevron.
 * `placeholder` is the empty option, shown in secondary text and never chosen again.
 */
export function SelectField({
  label,
  placeholder,
  options,
  hint,
  error,
  ...select
}: Omit<ComponentProps<"select">, "children"> & {
  label: string;
  placeholder: string;
  options: { value: string; label: string }[];
  hint?: string;
  error?: string;
}) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-small text-text-primary">
        {label}
      </label>
      <div className="relative">
        <select
          id={id}
          {...fieldAria(id, hint, error)}
          className={`${fieldControl} appearance-none pr-10 *:text-text-primary ${select.value ? "" : "text-text-secondary"}`}
          {...select}
        >
          <option value="" disabled>
            {placeholder}
          </option>
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        <ChevronDown
          aria-hidden="true"
          className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-text-secondary"
        />
      </div>
      <FieldNotes id={id} hint={hint} error={error} />
    </div>
  );
}

const draggingFiles = (event: DragEvent) => event.dataTransfer.types.includes("Files");

/**
 * A file picker (DESIGN.md Drop zone): a native button, so Enter or Space opens the system
 * picker, that also takes a dropped file (a pointer shortcut). `onFiles` gets every file chosen
 * or dropped; the caller checks them. Its name is the line it shows; the hint and the error
 * describe it.
 */
export function DropZone({
  file,
  size,
  accept,
  label,
  draggingLabel,
  hint,
  another,
  error,
  onFiles,
  ref,
}: {
  file: File | undefined;
  /** The chosen file's size, formatted. */
  size: string;
  accept: string;
  label: string;
  draggingLabel: string;
  hint: string;
  another: string;
  error: string | undefined;
  onFiles: (files: File[]) => void;
  ref?: Ref<HTMLButtonElement>;
}) {
  const id = useId();
  const input = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const describedBy = [!file && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(" ");

  const over = (event: DragEvent) => {
    if (!draggingFiles(event)) return;
    event.preventDefault();
    setDragging(true);
  };

  return (
    <div className="flex flex-col gap-1.5">
      <button
        ref={ref}
        type="button"
        aria-labelledby={`${id}-label`}
        aria-describedby={describedBy || undefined}
        onClick={() => input.current?.click()}
        onDragEnter={over}
        onDragOver={over}
        onDragLeave={(event) => {
          if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragging(false);
        }}
        onDrop={(event) => {
          if (!draggingFiles(event)) return;
          event.preventDefault();
          setDragging(false);
          onFiles([...event.dataTransfer.files]);
        }}
        className={`flex min-h-32 w-full flex-col items-center justify-center gap-1 rounded-control border p-4 text-center ${
          dragging
            ? "border-solid border-mint bg-teal-deep"
            : "border-dashed border-teal-border bg-inset hover:border-solid"
        }`}
      >
        {file && !dragging ? (
          <span id={`${id}-label`} className="flex w-full min-w-0 items-center gap-2 text-left">
            <FileText aria-hidden="true" className="size-5 shrink-0 text-text-secondary" />
            <span className="min-w-0 flex-1 truncate text-body font-medium">{file.name}</span>{" "}
            <span className="shrink-0 text-small text-text-secondary tabular-nums">{size}</span>{" "}
            <span className="shrink-0 text-small font-medium text-mint">{another}</span>
          </span>
        ) : (
          <>
            <Upload aria-hidden="true" className="size-5 text-text-secondary" />
            <span id={`${id}-label`} className="text-body font-medium">
              {dragging ? draggingLabel : label}
            </span>
            {!file && (
              <span id={`${id}-hint`} className="text-small text-text-secondary">
                {hint}
              </span>
            )}
          </>
        )}
      </button>
      <input
        ref={input}
        type="file"
        accept={accept}
        hidden
        tabIndex={-1}
        onChange={(event) => {
          onFiles([...(event.target.files ?? [])]);
          // The same file chosen again still counts.
          event.target.value = "";
        }}
      />
      <FieldNotes id={id} error={error} />
    </div>
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

/**
 * Numbered steps on an inset strip (DESIGN.md Stepper). The steps before `current` are done
 * (`doneLabel`, "Done:", is read before them); `current` has `aria-current="step"`. Below 640px
 * only the current step's label is visible.
 */
export function Stepper({
  label,
  steps,
  current,
  doneLabel,
}: {
  label: string;
  steps: string[];
  current: number;
  doneLabel: string;
}) {
  return (
    <ol aria-label={label} className="flex items-center gap-2 rounded-sm bg-inset px-3 py-2">
      {steps.map((step, index) => {
        const done = index < current;
        const now = index === current;
        const last = index === steps.length - 1;
        return (
          <li
            key={step}
            aria-current={now ? "step" : undefined}
            className={`flex items-center gap-2 ${last ? "" : "flex-1"}`}
          >
            {/* The list numbers its items for screen readers. */}
            <span
              aria-hidden="true"
              className={`flex size-5 shrink-0 items-center justify-center rounded-pill text-caption font-semibold tabular-nums ${
                done
                  ? "bg-mint"
                  : now
                    ? "border border-mint text-text-primary"
                    : "bg-elevated text-text-secondary"
              }`}
            >
              {done ? <Check className="size-3 text-on-mint" /> : index + 1}
            </span>
            {/* Below 640px only the current step shows its label; the others stay readable. */}
            <span
              className={`text-small whitespace-nowrap ${now ? "font-semibold text-text-primary" : "sr-only text-text-secondary sm:not-sr-only"}`}
            >
              {done && <span className="sr-only">{doneLabel} </span>}
              {step}
            </span>
            {/* Connectors shrink first, so three steps fit at 375px. */}
            {!last && (
              <span
                aria-hidden="true"
                className={`h-px min-w-3 flex-1 ${done ? "bg-mint" : "bg-neutral"}`}
              />
            )}
          </li>
        );
      })}
    </ol>
  );
}

const RING_RADIUS = 55;
const RING_LENGTH = 2 * Math.PI * RING_RADIUS;

/**
 * A 120px ring (DESIGN.md Radial progress): a mint arc from 12 o'clock and the percentage of
 * `value` out of `max`, rounded down so 100% shows only once all is done. The ring is a
 * progressbar named `label` that reads `valueText`, not a live region. Below it, `title` and its
 * `lines`.
 */
export function RadialProgress({
  value,
  max,
  label,
  valueText,
  title,
  lines,
}: {
  value: number;
  max: number;
  label: string;
  valueText: string;
  title: string;
  lines: string[];
}) {
  const done = Math.min(Math.max(value, 0), max);
  const ratio = max > 0 ? done / max : 0;
  // Integers first: 29 * 100 / 100 is 29, while 0.29 * 100 floors to 28.
  const percent = max > 0 ? Math.floor((done * 100) / max) : 0;
  return (
    <>
      <div
        role="progressbar"
        aria-label={label}
        // Indeterminate (no value) until there is a total.
        aria-valuemin={0}
        aria-valuemax={max > 0 ? max : undefined}
        aria-valuenow={max > 0 ? done : undefined}
        aria-valuetext={valueText}
        className="relative size-30 shrink-0"
      >
        <svg viewBox="0 0 120 120" aria-hidden="true" className="size-full -rotate-90">
          <circle
            cx="60"
            cy="60"
            r={RING_RADIUS}
            strokeWidth="10"
            className="fill-none stroke-chart-track"
          />
          {/* None at 0: a round cap would still draw a dot. */}
          {ratio > 0 && (
            <circle
              cx="60"
              cy="60"
              r={RING_RADIUS}
              strokeWidth="10"
              strokeLinecap="round"
              strokeDasharray={RING_LENGTH}
              strokeDashoffset={RING_LENGTH * (1 - ratio)}
              className="fill-none stroke-mint motion-safe:transition-[stroke-dashoffset] motion-safe:duration-300"
            />
          )}
        </svg>
        <span className="absolute inset-0 flex items-center justify-center text-value font-bold tabular-nums">
          {new Intl.NumberFormat(locale, { style: "percent" }).format(percent / 100)}
        </span>
      </div>
      <p className="mt-3 text-body font-semibold">{title}</p>
      {lines.map((line) => (
        <p key={line} className="text-small text-text-secondary tabular-nums">
          {line}
        </p>
      ))}
    </>
  );
}

const checklistGlyphs = {
  done: (
    <span
      aria-hidden="true"
      className="flex size-4 shrink-0 items-center justify-center rounded-pill bg-mint"
    >
      <Check className="size-3 text-on-mint" />
    </span>
  ),
  active: <Spinner />,
  pending: <Circle aria-hidden="true" className="size-4 shrink-0 text-neutral" />,
};

/**
 * A row of a checklist `<ul>` (DESIGN.md Checklist): the glyph for its `progress`, then `state`
 * ("Done:", "In progress:", "Not started:") for screen readers, the label and an optional count.
 */
export function ChecklistRow({
  progress,
  state,
  label,
  count,
}: {
  progress: keyof typeof checklistGlyphs;
  state: string;
  label: string;
  count?: string;
}) {
  return (
    <li className="flex min-h-9 items-center gap-3">
      {checklistGlyphs[progress]}
      <span className="flex-1 text-body text-text-secondary">
        <span className="sr-only">{state} </span>
        {label}
      </span>
      {count && <span className="text-small text-text-secondary tabular-nums">{count}</span>}
    </li>
  );
}

/** A relative time (DESIGN.md Time badge): `label` ("Due in 7 days") for `dateTime`. */
export function TimeBadge({ dateTime, label }: { dateTime: string; label: string }) {
  return (
    <time
      dateTime={dateTime}
      className="inline-flex h-5 items-center rounded-sm bg-info-bg px-2 text-caption whitespace-nowrap text-info tabular-nums"
    >
      {label}
    </time>
  );
}

/**
 * A stored passage in its original language, its quote highlighted (DESIGN.md Quote highlight).
 * `quote` shows the quote alone (the stored `quote` when the offsets don't map); `passage` the
 * whole text, the quote marked when they do. `markRef` gets the highlight, to scroll it into view.
 */
export function QuotedPassage({
  citation,
  mode,
  markRef,
  className = "",
}: {
  citation: Pick<Citation, "text" | "quote" | "quoteStart" | "quoteEnd" | "language">;
  mode: "quote" | "passage";
  markRef?: Ref<HTMLElement>;
  className?: string;
}) {
  const { text, language } = citation;
  const found = quoteFound(citation);
  const start = citation.quoteStart ?? 0;
  const end = citation.quoteEnd ?? 0;
  // The markers are read in the interface language, around a quote in its own.
  const quoted = (content: string) => (
    <>
      <span lang={locale} className="sr-only">
        {copy.source.quoteStart}
      </span>
      <mark ref={markRef} className="box-decoration-clone bg-teal-deep text-text-primary">
        {content}
      </mark>
      <span lang={locale} className="sr-only">
        {copy.source.quoteEnd}
      </span>
    </>
  );
  if (mode === "quote")
    return (
      <blockquote
        lang={language}
        className={`rounded-sm bg-inset p-3 text-body whitespace-pre-line ${className}`}
      >
        {quoted(found ? text.slice(start, end) : citation.quote)}
      </blockquote>
    );
  return (
    <div
      lang={language}
      className={`rounded-sm bg-inset p-4 text-body whitespace-pre-line ${className}`}
    >
      {found ? (
        <>
          {text.slice(0, start)}
          {quoted(text.slice(start, end))}
          {text.slice(end)}
        </>
      ) : (
        text
      )}
    </div>
  );
}
