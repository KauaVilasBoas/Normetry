import {
  ChevronRight,
  Pill,
  Stethoscope,
  Store,
  UtensilsCrossed,
  type LucideIcon,
} from "lucide-react";
import type { ReactNode } from "react";
import { Link } from "react-router";

/** A table header cell (DESIGN.md Table, on the inset header row). */
export const th = "h-9 px-3 text-left text-caption font-medium text-text-muted";

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

/** 28px inset tile with a decorative icon (a KPI card's, an establishment's type). */
export function IconTile({ Icon }: { Icon: LucideIcon }) {
  return (
    <span className="flex size-7 shrink-0 items-center justify-center rounded-sm border border-border bg-inset">
      <Icon aria-hidden="true" className="size-4 text-chart-2" />
    </span>
  );
}

/** 28px tile with the establishment type icon. */
export function TypeTile({ type }: { type: string }) {
  return <IconTile Icon={typeIcons[type] ?? Store} />;
}

/**
 * A KPI card inside a `<dl>` (DESIGN.md KPI card): the label and its icon tile are the `<dt>`,
 * the value and the footer (a delta, a pill, a caption) are `<dd>`s.
 */
export function KpiCard({
  label,
  Icon,
  value,
  children,
}: {
  label: string;
  Icon: LucideIcon;
  value: ReactNode;
  children?: ReactNode;
}) {
  return (
    <div className="flex flex-col rounded-card border border-border bg-card p-4 motion-safe:animate-fade-in">
      <dt className="flex items-start justify-between gap-3 text-body text-text-secondary">
        {label}
        <IconTile Icon={Icon} />
      </dt>
      <dd className="mt-2 text-kpi font-bold tabular-nums">{value}</dd>
      <dd className="mt-auto flex flex-wrap items-center gap-x-2 gap-y-1 pt-2 text-small text-text-secondary">
        {children}
      </dd>
    </div>
  );
}

/** A native `<details>`: Enter or Space opens it; the chevron turns (not under reduced motion). */
export function Disclosure({
  summary,
  className,
  children,
}: {
  summary: string;
  className: string;
  children: ReactNode;
}) {
  return (
    <details className={`group ${className}`}>
      <summary className="inline-flex min-h-6 cursor-pointer list-none items-center gap-1 rounded-sm text-small font-medium text-mint [&::-webkit-details-marker]:hidden">
        <ChevronRight
          aria-hidden="true"
          className="size-3.5 shrink-0 group-open:rotate-90 motion-safe:transition-transform"
        />
        {summary}
      </summary>
      {children}
    </details>
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
