import { inspectionStatuses, problemCodes } from "../api/contract";
import type { Establishment, InspectionSummary, ThemeProgress } from "../api/queries";
import { copy, format } from "../copy";
import type { Sent } from "../settings/profiles";

/** The core's inspections hub and its event (spec §5). */
export const HUB_URL = "/hubs/inspections";
export const HUB_EVENT = "InspectionChanged";

/** What the hub pushes after each change of an inspection (InspectionChange in the core). */
export interface InspectionChange {
  inspectionId: string;
  establishmentId: string;
  status: string;
  itemsEvaluated: number;
  itemsTotal: number;
  themes: ThemeProgress[];
  failureReasonCode: string | null;
}

export const LIST_TITLE_ID = "inspections-title";
export const DETAIL_TITLE_ID = "inspection-title";
export const FINDINGS_TITLE_ID = "findings-title";

/** Waiting or Running: live, and no other inspection may start. */
export const inFlight = (status: string) =>
  status === inspectionStatuses.requested || status === inspectionStatuses.running;

export const statusLabel = (status: string) => copy.inspections.statuses[status] ?? status;

export const failureText = (code: string | null) =>
  copy.inspections.failures[code ?? ""] ?? copy.inspections.failureUnknown;

export const regulationOf = ({ jurisdiction }: Establishment) =>
  copy.jurisdictions[jurisdiction]?.regulation ?? jurisdiction;

export const findingCount = (count: number) =>
  count === 1 ? copy.findings.one : format(copy.findings.count, { count: String(count) });

const counts = (evaluated: number, total: number) => ({
  evaluated: String(evaluated),
  total: String(total),
});

/** The status cell's caption: the count while running, the short reason once failed. */
export function statusCaption(row: InspectionSummary): string | undefined {
  if (row.status === inspectionStatuses.running && row.itemsTotal > 0)
    return format(copy.inspections.checked, counts(row.itemsEvaluated, row.itemsTotal));
  if (row.status === inspectionStatuses.failed) return failureText(row.failureReasonCode).short;
  return undefined;
}

/** The Results cell: the verdict counts once completed. */
export function resultText({ status, verdictCounts: v }: InspectionSummary): string | undefined {
  if (status !== inspectionStatuses.completed) return undefined;
  return format(copy.inspections.resultCounts, {
    nonCompliant: String(v.nonCompliant),
    insufficient: String(v.insufficientEvidence),
    compliant: String(v.compliant),
  });
}

/** The list row a started inspection shows as until the refetch brings the server's. */
export const requestedRow = (id: string, regulation: string): InspectionSummary => ({
  id,
  status: inspectionStatuses.requested,
  regulation,
  requestedAt: new Date().toISOString(),
  startedAt: null,
  completedAt: null,
  itemsTotal: 0,
  itemsEvaluated: 0,
  verdictCounts: { compliant: 0, nonCompliant: 0, insufficientEvidence: 0 },
  failureReasonCode: null,
});

/** Why Run was refused (spec §4). A quota or a rate limit holds Run `until` then. */
export type Refusal =
  | { kind: "quota" | "rateLimited"; until: number }
  | { kind: "noDocuments" | "capacity" | "denied" | "error" };

/** The refusal of a start, or "running" for a stale list (refetching it shows the running row). */
export function runRefusal(result: Sent & { ok: false }, now: number): Refusal | "running" {
  if (result.status === 429)
    return {
      kind: result.code === problemCodes.inspectionQuota ? "quota" : "rateLimited",
      // ponytail: a 429 without Retry-After waits a minute; the core always sends one today.
      until: now + (result.retryAfter ?? 60) * 1000,
    };
  if (result.status === 409 && result.code === problemCodes.inspectionRunning) return "running";
  if (result.status === 409 && result.code === problemCodes.noIndexedDocuments)
    return { kind: "noDocuments" };
  // The tenant's other establishments fill its in-flight cap: nothing here to refetch.
  if (result.status === 409 && result.code === problemCodes.inspectionCapacity)
    return { kind: "capacity" };
  if (result.status === 403) return { kind: "denied" };
  return { kind: "error" };
}

/**
 * The running card by state (spec §4): the stepper's current step and the radial's title and
 * body. Step 2 starts once every item is checked; step 3 never shows as current.
 */
export function runningView(
  {
    status,
    itemsEvaluated,
    itemsTotal,
    regulation,
  }: Pick<InspectionSummary, "status" | "itemsEvaluated" | "itemsTotal" | "regulation">,
  establishment: string,
) {
  if (status !== inspectionStatuses.running)
    return {
      step: 0,
      title: copy.inspections.waitingTitle,
      body: copy.inspections.waitingBody,
    };
  if (itemsTotal > 0 && itemsEvaluated >= itemsTotal)
    return {
      step: 1,
      title: copy.inspections.preparingTitle,
      body: copy.inspections.preparingBody,
    };
  return {
    step: 0,
    title: copy.inspections.progressTitle,
    body: format(copy.inspections.progressBody, { establishment, regulation }),
  };
}

/** A code's place in `order`; unknown codes after every known one. */
export const rankIn = (order: string[]) => (code: string) => {
  const index = order.indexOf(code);
  return index < 0 ? order.length : index;
};

export const themeRank = rankIn(Object.keys(copy.themes));
const themeStates = {
  done: copy.inspections.themeDone,
  active: copy.inspections.themeActive,
  pending: copy.inspections.themePending,
};

/** The theme checklist in display order (unknown themes last, as sent), without empty themes. */
export function checklist(themes: ThemeProgress[]) {
  return themes
    .filter((theme) => theme.itemsTotal > 0)
    .sort((a, b) => themeRank(a.theme) - themeRank(b.theme))
    .map(({ theme, itemsTotal, itemsEvaluated }) => {
      const progress =
        itemsEvaluated >= itemsTotal ? "done" : itemsEvaluated > 0 ? "active" : "pending";
      return {
        theme,
        progress,
        state: themeStates[progress],
        label: copy.themes[theme] ?? theme,
        count: format(copy.inspections.themeCount, counts(itemsEvaluated, itemsTotal)),
      } as const;
    });
}

/** The highest of 25, 50 and 75 percent reached (0 below 25), for the announcements. */
export const milestone = (evaluated: number, total: number) =>
  total > 0 ? ([75, 50, 25].find((step) => evaluated * 100 >= step * total) ?? 0) : 0;

export const milestoneText = (evaluated: number, total: number) =>
  format(copy.inspections.milestone, counts(evaluated, total));

export const progressText = (evaluated: number, total: number) =>
  format(copy.inspections.progress, counts(evaluated, total));
