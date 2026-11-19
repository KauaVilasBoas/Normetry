import { Circle, CircleCheck, CircleX, Clock, Diamond, Pencil } from "lucide-react";
import type { ReactNode } from "react";
import { Spinner } from "./feedback";
import { pillVariant, type PillVariant } from "./statuses";

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

/** A plain label ("System", a profile name). Not a status, so never a status color. */
export function Tag({ children }: { children: ReactNode }) {
  return (
    <span className="inline-block h-5 max-w-full shrink-0 truncate rounded-sm bg-inset px-2 text-caption leading-5 font-medium text-text-secondary">
      {children}
    </span>
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
