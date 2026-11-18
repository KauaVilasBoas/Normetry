import { useQueryClient } from "@tanstack/react-query";
import type { RequestOptions } from "../api/client";
import {
  inspectionKey,
  inspectionsKey,
  type ActionItem,
  type Finding,
  type InspectionDetail,
} from "../api/queries";
import { calendarDate, copy, dateTime, format, localDate, locale, relativeDays } from "../copy";
import type { Sent } from "../settings/profiles";
import { findingCount, rankIn, themeRank } from "./inspections";

// The completed view (spec inspection-screen.md §4, §5): order, filters, due dates, edit rules.

export const severityOrder = ["critical", "major", "minor"];
export const verdictOrder = ["non_compliant", "insufficient_evidence", "compliant"];
const severityRank = rankIn(severityOrder);
const verdictRank = rankIn(verdictOrder);

export const verdictLabel = (code: string) => copy.findings.verdicts[code] ?? code;
export const severityLabel = (code: string) => copy.findings.severities[code] ?? code;
export const reviewLabel = (status: string) => copy.findings.reviews[status] ?? status;
export const confidenceLabel = (code: string) => copy.findings.confidences[code] ?? code;
export const themeLabel = (code: string) => copy.themes[code] ?? code;

const byTitle = (a: Finding, b: Finding) => a.title.localeCompare(b.title, locale);

/** Critical first (minor first when `ascending`), then non-compliant first, then title A–Z. */
export function sortFindings(findings: Finding[], ascending = false): Finding[] {
  const direction = ascending ? -1 : 1;
  return [...findings].sort(
    (a, b) =>
      direction * (severityRank(a.severity) - severityRank(b.severity)) ||
      verdictRank(a.verdict) - verdictRank(b.verdict) ||
      byTitle(a, b),
  );
}

/** The two filters; "" is all. They combine. */
export interface Filters {
  verdict: string;
  theme: string;
}

export const matches =
  ({ verdict, theme }: Filters) =>
  (finding: Finding) =>
    (!verdict || finding.verdict === verdict) && (!theme || finding.theme === theme);

const option = (value: string, label: string, count: number) => ({
  value,
  label: format(copy.findings.option, { label, count: String(count) }),
});

/** All verdicts, each with its count over every finding. */
export function verdictOptions(findings: Finding[]) {
  return [
    { value: "", label: copy.findings.allVerdicts },
    ...verdictOrder.map((verdict) =>
      option(verdict, verdictLabel(verdict), findings.filter((f) => f.verdict === verdict).length),
    ),
  ];
}

/** The themes present, in checklist order (unknown ones last), each with its count. */
export function themeOptions(findings: Finding[]) {
  const themes = [...new Set(findings.map((f) => f.theme))].sort(
    (a, b) => themeRank(a) - themeRank(b),
  );
  return [
    { value: "", label: copy.findings.allThemes },
    ...themes.map((theme) =>
      option(theme, themeLabel(theme), findings.filter((f) => f.theme === theme).length),
    ),
  ];
}

/** "43 findings · 12 pending review", or "· All reviewed". */
export function findingsCaption(findings: Finding[]): string {
  const pending = findings.filter((f) => f.reviewStatus === "Pending").length;
  return format(copy.findings.caption, {
    findings: findingCount(findings.length),
    review:
      pending > 0
        ? format(copy.findings.pending, { count: String(pending) })
        : copy.findings.allReviewed,
  });
}

/** An open action's badge: "Due in 7 days" from today on (local dates), "Overdue" before. */
export function dueBadge(dueOn: string, now = new Date()): { overdue: boolean; label: string } {
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  // Rounded: a day across a daylight saving change is 23 or 25 hours.
  const days = Math.round((localDate(dueOn).getTime() - today.getTime()) / 86_400_000);
  return days < 0
    ? { overdue: true, label: copy.actions.overdue }
    : { overdue: false, label: format(copy.actions.dueIn, { when: relativeDays(days) }) };
}

/**
 * The action plan: the actions of findings that are not Rejected, by due date, then severity, then
 * finding title. Status plays no part, so a toggle never reorders it.
 */
export function actionPlan(findings: Finding[]) {
  return findings
    .filter(
      (f): f is Finding & { actionItem: ActionItem } =>
        f.actionItem !== null && f.reviewStatus !== "Rejected",
    )
    .sort(
      (a, b) =>
        a.actionItem.dueOn.localeCompare(b.actionItem.dueOn) ||
        severityRank(a.severity) - severityRank(b.severity) ||
        byTitle(a, b),
    );
}

const reviewedBy: Record<string, string | undefined> = {
  Accepted: copy.findings.acceptedBy,
  Rejected: copy.findings.rejectedBy,
  Edited: copy.findings.editedBy,
};

/** "Accepted by {name} on {date}.", or "No one has reviewed this finding yet." */
export function reviewSentence({ reviewStatus, reviewedBy: by, reviewedAt }: Finding): string {
  const template = reviewedBy[reviewStatus];
  if (!template || !reviewedAt) return copy.findings.notReviewed;
  return format(template, {
    name: by?.displayName ?? copy.findings.unknownReviewer,
    date: dateTime(reviewedAt),
  });
}

/** The original value of each field an edit changed; none before the first edit. */
export function originalValues({ original, verdict, severity, rationale, actionItem }: Finding) {
  if (!original) return [];
  return [
    {
      label: copy.findings.verdict,
      changed: original.verdict !== verdict,
      value: verdictLabel(original.verdict),
    },
    {
      label: copy.findings.severity,
      changed: original.severity !== severity,
      value: severityLabel(original.severity),
    },
    {
      label: copy.findings.rationale,
      changed: original.rationale !== rationale,
      value: original.rationale,
    },
    {
      label: copy.findings.action,
      changed: original.actionText !== (actionItem?.text ?? null),
      value: original.actionText ?? copy.findings.noAction,
    },
    // No original action: the Action row already says so.
    {
      label: copy.findings.dueDate,
      changed: original.dueOn !== null && original.dueOn !== (actionItem?.dueOn ?? null),
      value: original.dueOn ? calendarDate(original.dueOn) : "",
    },
  ].filter((field) => field.changed);
}

/** The Edit dialog's fields; the action fields are "" when the finding has no action. */
export interface EditDraft {
  verdict: string;
  severity: string;
  rationale: string;
  actionText: string;
  dueOn: string;
}

export const draftOf = (finding: Finding): EditDraft => ({
  verdict: finding.verdict,
  severity: finding.severity,
  rationale: finding.rationale,
  actionText: finding.actionItem?.text ?? "",
  dueOn: finding.actionItem?.dueOn ?? "",
});

/** What the edit changes, texts trimmed: only these fields are sent. */
export function editChanges(finding: Finding, draft: EditDraft): Partial<EditDraft> {
  const before = draftOf(finding);
  const changes: Partial<EditDraft> = {};
  for (const key of Object.keys(draft) as (keyof EditDraft)[]) {
    const value = draft[key].trim();
    if (value !== before[key].trim()) changes[key] = value;
  }
  return changes;
}

export type EditErrors = Partial<Record<keyof EditDraft, string>>;

/** Fields in form order, for focusing the first invalid one. */
export const editFields = ["verdict", "severity", "rationale", "actionText", "dueOn"] as const;

/**
 * A rationale is required. An action and its due date go together: both or neither, and neither
 * only for a finding that has no action.
 */
export function editErrors(finding: Finding, draft: EditDraft): EditErrors {
  const text = draft.actionText.trim();
  const keepsAction = finding.actionItem !== null;
  const errors: EditErrors = {};
  if (!draft.rationale.trim()) errors.rationale = copy.findings.rationaleRequired;
  if (!text && (keepsAction || draft.dueOn)) errors.actionText = copy.findings.actionRequired;
  if (!draft.dueOn && (keepsAction || text)) errors.dueOn = copy.findings.dueRequired;
  return errors;
}

export type Refusal = Sent & { ok: false };

/** A review's refusal, as the sheet says it. */
export function reviewError({ status }: Refusal): string {
  if (status === 403) return copy.inspections.denied;
  if (status === 404) return copy.findings.gone;
  return copy.findings.reviewError;
}

/**
 * Sends a review or an action toggle (`path` under the inspection) and patches the finding it
 * answers into the cached inspection. With `refetch`, the inspection and the list are fetched again
 * afterwards. Resolves to the refusal, or undefined once saved.
 */
export function useReview(
  establishmentId: string,
  inspectionId: string,
  send: (path: string, options: RequestOptions) => Promise<Sent>,
  { refetch = true } = {},
) {
  const queryClient = useQueryClient();
  const key = inspectionKey(establishmentId, inspectionId);
  return async (path: string, options: RequestOptions): Promise<Refusal | undefined> => {
    const result = await send(
      `/establishments/${encodeURIComponent(establishmentId)}/inspections/${encodeURIComponent(inspectionId)}/${path}`,
      options,
    );
    if (!result.ok) return result;
    const finding = (await result.response.json().catch(() => undefined)) as Finding | undefined;
    if (finding?.id) {
      // A fetch started before the change would land on the patch with the old finding.
      await queryClient.cancelQueries({ queryKey: key });
      queryClient.setQueryData<InspectionDetail>(
        key,
        (detail) =>
          detail && {
            ...detail,
            findings: detail.findings.map((f) => (f.id === finding.id ? finding : f)),
          },
      );
    }
    if (refetch || !finding?.id) void queryClient.invalidateQueries({ queryKey: key });
    if (refetch) void queryClient.invalidateQueries({ queryKey: inspectionsKey(establishmentId) });
    return undefined;
  };
}
