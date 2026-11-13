import { ArrowRight, Pill, Stethoscope, Store, UtensilsCrossed } from "lucide-react";
import { Dialog } from "radix-ui";
import type { ButtonHTMLAttributes, ComponentProps, ReactNode } from "react";

const buttonVariants = {
  primary: "bg-mint text-on-mint font-semibold hover:bg-mint-hover",
  secondary: "border border-teal-border text-text-primary font-medium",
};

/**
 * DESIGN.md buttons: one primary per view; text on mint is always --on-mint. A secondary button
 * hovers to --bg-elevated, or to --bg-inset when it sits on --bg-elevated (`onElevated`).
 */
export function Button({
  variant,
  onElevated = false,
  className = "",
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant: keyof typeof buttonVariants;
  onElevated?: boolean;
}) {
  const hover = variant === "primary" ? "" : onElevated ? "hover:bg-inset" : "hover:bg-elevated";
  return (
    <button
      type="button"
      className={`inline-flex h-9 items-center justify-center gap-2 rounded-control px-4 text-body ${buttonVariants[variant]} ${hover} ${className}`}
      {...props}
    />
  );
}

/** Indeterminate ring, 16px; static under reduced motion. */
export function Spinner() {
  return (
    <span
      aria-hidden="true"
      className="inline-block size-4 shrink-0 rounded-pill border-2 border-elevated border-t-mint motion-safe:animate-spin"
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

/** Empty and error block: optional icon, title and body. */
export function Message({
  icon,
  title,
  body,
  className = "mt-4 items-start",
}: {
  icon?: ReactNode;
  title: string;
  body: string;
  className?: string;
}) {
  return (
    <div className={`flex flex-col gap-2 ${className}`}>
      {icon}
      <h3 className="text-title font-semibold">{title}</h3>
      <p className="text-body text-text-secondary">{body}</p>
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
      className={`inline-flex h-7 max-w-full items-center gap-1.5 rounded-sm border border-teal-border px-2.5 text-small font-medium text-mint focus-visible:-outline-offset-2 ${
        expanded ? "bg-teal-deep" : "hover:bg-inset"
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
