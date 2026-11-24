import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { useSearchParams } from "react-router";
import type { RequestOptions } from "../api/client";
import { actionStatuses, reviewStatuses, severities, verdicts } from "../api/contract";
import {
  actionItemPaths,
  inspectionKey,
  inspectionsKey,
  type ActionItem,
  type Finding,
  type InspectionDetail,
} from "../api/inspections";
import { useSend, type Sent } from "../api/send";
import { calendarDate, copy, dateTime, format, localDate, locale, relativeDays } from "../copy";
import { findingFilterParams, hasFindingFilter } from "../routes";
import { useAnnouncer } from "../ui/announcer";
import { focusById } from "../ui/focus";
import { FINDINGS_TITLE_ID, findingCount, rankIn, themeRank } from "./inspections";

// The completed view (spec inspection-screen.md §4, §5): order, filters, due dates, edit rules.

export const severityOrder = [severities.critical, severities.major, severities.minor];
export const verdictOrder = [
  verdicts.nonCompliant,
  verdicts.insufficientEvidence,
  verdicts.compliant,
];
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

const noFilters: Filters = { verdict: "", theme: "" };

/**
 * The Findings card's filters. `?verdict=` and `?theme=` (the Overview's links) preset them once,
 * when a code is among the options; then they leave the URL, where filters never stay. Clearing
 * moves focus to the heading first, since Clear filters goes with them.
 */
export function usePresetFilters(findings: Finding[]) {
  const [params, setParams] = useSearchParams();
  const [filters, setFilters] = useState<Filters>(() => {
    const known = (options: { value: string }[], value: string | null) =>
      options.some((option) => option.value === value) ? (value ?? "") : "";
    return {
      verdict: known(verdictOptions(findings), params.get(findingFilterParams.verdict)),
      theme: known(themeOptions(findings), params.get(findingFilterParams.theme)),
    };
  });
  const filtered = hasFindingFilter(params);

  useEffect(() => {
    if (filtered)
      setParams(
        (next) => {
          next.delete(findingFilterParams.verdict);
          next.delete(findingFilterParams.theme);
          return next;
        },
        { replace: true },
      );
  }, [filtered, setParams]);

  const filterBy = (filter: keyof Filters, value: string) =>
    setFilters({ ...filters, [filter]: value });

  const clearFilters = () => {
    focusById(FINDINGS_TITLE_ID);
    setFilters(noFilters);
  };

  return { filters, filterBy, clearFilters };
}

/** A findings table row's button, where focus goes back when the sheet closes. */
export const rowId = (findingId: string) => `finding-${findingId}`;

/**
 * The finding sheet's open state. Previous and Next walk `rows` as they were when it opened, each
 * move said in the sheet's own polite region (the page's is hidden while it is open); closing
 * returns focus to the row shown last, or to the heading when that row is filtered out by then
 * (`afterClose`). Its changes are sent `inside` it: a 403 refetches /me once it closes.
 */
export function useFindingSheet(findings: Finding[], rows: Finding[]) {
  const [sheet, setSheet] = useState<{ ids: string[]; index: number }>();
  const { announcements, announce, clear } = useAnnouncer();
  const lastShown = useRef<string>(undefined);
  const { send, closed } = useSend({ inside: true });
  const shown = sheet && findings.find((f) => f.id === sheet.ids[sheet.index]);

  const open = (index: number) => {
    lastShown.current = rows[index].id;
    clear();
    setSheet({ ids: rows.map((row) => row.id), index });
  };

  const move = (index: number) => {
    if (!sheet) return;
    const next = findings.find((f) => f.id === sheet.ids[index]);
    if (!next) return;
    lastShown.current = next.id;
    setSheet({ ...sheet, index });
    announce(
      format(copy.findings.moved, {
        title: next.title,
        n: String(index + 1),
        total: String(sheet.ids.length),
      }),
    );
  };

  const close = () => {
    setSheet(undefined);
    closed();
  };

  const openChange = (next: boolean) => {
    if (!next) close();
  };

  const afterClose = (event: Event) => {
    event.preventDefault();
    focusById(rowId(lastShown.current ?? ""), FINDINGS_TITLE_ID);
  };

  return {
    shown,
    n: (sheet?.index ?? 0) + 1,
    total: sheet?.ids.length ?? 0,
    /** Undefined at the first finding. */
    previous: sheet && sheet.index > 0 ? () => move(sheet.index - 1) : undefined,
    /** Undefined at the last finding. */
    next: sheet && sheet.index < sheet.ids.length - 1 ? () => move(sheet.index + 1) : undefined,
    open,
    close,
    openChange,
    afterClose,
    announcements,
    announce,
    send,
  };
}

/** "43 findings · 12 pending review", or "· All reviewed". */
export function findingsCaption(findings: Finding[]): string {
  const pending = findings.filter((f) => f.reviewStatus === reviewStatuses.pending).length;
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
        f.actionItem !== null && f.reviewStatus !== reviewStatuses.rejected,
    )
    .sort(
      (a, b) =>
        a.actionItem.dueOn.localeCompare(b.actionItem.dueOn) ||
        severityRank(a.severity) - severityRank(b.severity) ||
        byTitle(a, b),
    );
}

const reviewedBy: Record<string, string | undefined> = {
  [reviewStatuses.accepted]: copy.findings.acceptedBy,
  [reviewStatuses.rejected]: copy.findings.rejectedBy,
  [reviewStatuses.edited]: copy.findings.editedBy,
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
 * Sends a review or an action toggle to `path` (from `findingPaths` or `actionItemPaths`) and
 * patches the finding it answers into the cached inspection. With `refetch`, the inspection and
 * the list are fetched again afterwards. Resolves to the refusal, or undefined once saved.
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
    const result = await send(path, options);
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

/**
 * The action plan (`actionPlan`) and its done toggles: a toggle shows at once, posts, and reverts
 * with the row's error when it fails. While its request runs, more clicks on it are ignored.
 */
export function useActionToggles(inspection: InspectionDetail) {
  const { send } = useSend();
  const review = useReview(inspection.establishmentId, inspection.id, send, { refetch: false });
  // The status each toggle in flight shows until its answer, by action id.
  const [toggled, setToggled] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const busy = useRef(new Set<string>());
  const rows = actionPlan(inspection.findings);
  const isDone = (action: ActionItem) =>
    (toggled[action.id] ?? action.status) === actionStatuses.done;

  const settle = (id: string) =>
    setToggled((map) => {
      const rest = { ...map };
      delete rest[id];
      return rest;
    });

  const toggle = async (action: ActionItem, checked: boolean) => {
    if (busy.current.has(action.id)) return;
    busy.current.add(action.id);
    setToggled((map) => ({
      ...map,
      [action.id]: checked ? actionStatuses.done : actionStatuses.open,
    }));
    setErrors((map) => ({ ...map, [action.id]: undefined }));
    const toggles = actionItemPaths(inspection.establishmentId, inspection.id, action.id);
    const refusal = await review(checked ? toggles.done : toggles.reopen, { method: "POST" });
    busy.current.delete(action.id);
    settle(action.id);
    if (refusal)
      setErrors((map) => ({
        ...map,
        [action.id]: refusal.status === 403 ? copy.inspections.denied : copy.actions.error,
      }));
  };

  return {
    rows,
    done: rows.filter((row) => isDone(row.actionItem)).length,
    isDone,
    errors,
    toggle,
  };
}
