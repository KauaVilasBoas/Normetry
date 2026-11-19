import { Check, Circle } from "lucide-react";
import { locale } from "../copy";
import { Spinner } from "./feedback";

/** DESIGN.md Progress bar, decorative: the percentage beside it carries the value. */
export function ProgressBar({ ratio }: { ratio: number }) {
  return (
    <span
      aria-hidden="true"
      className="block h-1.5 w-16 shrink-0 rounded-pill bg-chart-track sm:w-24"
    >
      {ratio > 0 && (
        // The width is a data value, so an inline style.
        <span
          className="block h-full rounded-pill bg-chart-1"
          style={{ width: `${Math.min(ratio, 1) * 100}%` }}
        />
      )}
    </span>
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
