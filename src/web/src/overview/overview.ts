import type { CompliancePoint, ThemeVerdicts, VerdictCounts } from "../api/overview";
import { copy, format, percent, shortDay } from "../copy";

// The overview's numbers (spec overview-screen.md §4).

export const TITLE_ID = "overview-title";

/** Whole points of `part` in `total`, rounded down, from integers (29 of 100 is 29, not 28). */
export const share = (part: number, total: number) =>
  total > 0 ? Math.floor((part * 100) / total) : 0;

/** A ratio's whole points, rounded down as `percent()` shows it. */
const points = (ratio: number) => Math.floor(ratio * 100 + 1e-9);

/** "77%" for whole points. */
export const pointsText = (value: number) => percent(value / 100);

export const verdictTotal = (counts: VerdictCounts) =>
  counts.compliant + counts.nonCompliant + counts.insufficientEvidence;

/**
 * The compliance change from the nearest earlier inspection with a result to the latest (the
 * last point), in the points shown on screen. Undefined without a latest result; null without
 * an earlier one.
 */
export function delta(series: CompliancePoint[]): number | null | undefined {
  const latest = series.at(-1)?.compliance;
  if (latest === null || latest === undefined) return undefined;
  const previous = series.slice(0, -1).findLast((point) => point.compliance !== null)?.compliance;
  if (previous === null || previous === undefined) return null;
  return points(latest) - points(previous);
}

/** A theme row's compliant share in whole points, and its counts line. */
export const themeShare = (row: ThemeVerdicts) => share(row.compliant, verdictTotal(row));

export const themeCounts = (row: VerdictCounts) =>
  format(copy.inspections.resultCounts, {
    nonCompliant: String(row.nonCompliant),
    insufficient: String(row.insufficientEvidence),
    compliant: String(row.compliant),
  });

/** The chart's points: the inspections with a result, oldest first. */
export function plotted(series: CompliancePoint[], now = new Date()) {
  return series.flatMap(({ inspectionId, completedAt, compliance }) => {
    if (compliance === null) return [];
    const label = shortDay(completedAt, now);
    return [
      {
        id: inspectionId,
        label,
        ratio: compliance,
        title: format(copy.overview.point, { date: label, value: percent(compliance) }),
      },
    ];
  });
}

export type RowThreeCard = "trend" | "documents" | "risk";

// Literal classes, so Tailwind finds them. Alone a card fills the row; with two cards the chart
// takes 7 of the 12 columns, with three 5.
const alone = "md:col-span-2 xl:col-span-12";
const spans = {
  1: { trend: alone, documents: alone, risk: alone },
  2: {
    trend: "md:col-span-2 xl:col-span-7",
    documents: "md:col-span-2 xl:col-span-5",
    risk: "md:col-span-2 xl:col-span-5",
  },
  3: { trend: "md:col-span-2 xl:col-span-5", documents: "xl:col-span-3", risk: "xl:col-span-4" },
};

/** Each card's column span in row 3's 2-column (768px) and 12-column (1280px) grids. */
export const rowThreeSpans = (cards: RowThreeCard[]): Record<RowThreeCard, string> =>
  spans[Math.min(Math.max(cards.length, 1), 3) as 1 | 2 | 3];
