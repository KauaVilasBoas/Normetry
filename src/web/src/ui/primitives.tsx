import { Pill, Stethoscope, Store, UtensilsCrossed } from "lucide-react";
import type { ButtonHTMLAttributes } from "react";

const buttonVariants = {
  primary: "bg-mint text-on-mint font-semibold hover:bg-mint-hover",
  secondary: "border border-teal-border text-text-primary font-medium hover:bg-elevated",
};

/** DESIGN.md buttons: one primary per view; text on mint is always --on-mint. */
export function Button({
  variant,
  className = "",
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant: keyof typeof buttonVariants }) {
  return (
    <button
      type="button"
      className={`inline-flex h-9 items-center justify-center gap-2 rounded-control px-4 text-body ${buttonVariants[variant]} ${className}`}
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
