import { useQuery } from "@tanstack/react-query";
import { useSession } from "../auth/session";
import type { ApiClient } from "./client";
import type { StoredPassage } from "./passages";
import { establishmentPath } from "./queries";

/** An inspection's progress in one checklist theme (a contract code, "temperature_control"). */
export interface ThemeProgress {
  theme: string;
  itemsTotal: number;
  itemsEvaluated: number;
}

/**
 * A row of GET /establishments/{e}/inspections (newest first). `status` is Requested (shown as
 * Waiting), Running, Completed or Failed; `failureReasonCode` a queue contract reason code or
 * `timed_out`. Unknown values show as sent (statuses) or with the generic reason (codes).
 */
export interface InspectionSummary {
  id: string;
  status: string;
  regulation: string;
  requestedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  itemsTotal: number;
  itemsEvaluated: number;
  verdictCounts: { compliant: number; nonCompliant: number; insufficientEvidence: number };
  failureReasonCode: string | null;
}

/** A copy of the regulation passage a finding cites, kept with it; quoted in its own language. */
export interface RegulationCitation extends StoredPassage {
  sourceTitle: string;
  sourceVersion: string;
}

/** A copy of a passage of the establishment's own document. The title is null on old uploads. */
export interface EvidenceCitation extends StoredPassage {
  documentId: string;
  documentTitle: string | null;
  documentType: string | null;
  versionNumber: number;
}

export interface UserReference {
  id: string;
  displayName: string | null;
}

/** `dueOn` is a date with no time ("2026-11-23"); `status` Open or Done. */
export interface ActionItem {
  id: string;
  text: string;
  dueOn: string;
  status: string;
  updatedBy: UserReference | null;
  updatedAt: string | null;
}

/**
 * A finding, as GET inspection lists it and every review route returns it. Codes (theme, severity,
 * verdict, confidence) are the contract's snake_case; `reviewStatus` Pending, Accepted, Rejected
 * or Edited. `original` holds the values before the first edit.
 */
export interface Finding {
  id: string;
  itemId: string;
  theme: string;
  title: string;
  severity: string;
  verdict: string;
  confidence: string;
  rationale: string;
  regulationCitations: RegulationCitation[];
  evidenceCitations: EvidenceCitation[];
  reviewStatus: string;
  reviewedBy: UserReference | null;
  reviewedAt: string | null;
  reviewNote: string | null;
  original: {
    verdict: string;
    severity: string;
    rationale: string;
    actionText: string | null;
    dueOn: string | null;
  } | null;
  actionItem: ActionItem | null;
}

/** GET /establishments/{e}/inspections/{id}. */
export interface InspectionDetail {
  id: string;
  establishmentId: string;
  status: string;
  regulation: string;
  requestedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  itemsTotal: number;
  itemsEvaluated: number;
  themes: ThemeProgress[];
  failureReasonCode: string | null;
  findings: Finding[];
}

/** GET the establishment's history; POST runs an inspection. */
export const inspectionsPath = (establishmentId: string) =>
  `${establishmentPath(establishmentId)}/inspections`;
/** GET one inspection. */
export const inspectionPath = (establishmentId: string, id: string) =>
  `${inspectionsPath(establishmentId)}/${encodeURIComponent(id)}`;

/** A finding's review: PATCH edits it, POST accepts or rejects it. */
export function findingPaths(establishmentId: string, inspectionId: string, findingId: string) {
  const inspection = inspectionPath(establishmentId, inspectionId);
  const finding = `${inspection}/findings/${encodeURIComponent(findingId)}`;
  return { edit: finding, accept: `${finding}/accept`, reject: `${finding}/reject` };
}

/** An action item's toggle (POST): done, or open again. */
export function actionItemPaths(establishmentId: string, inspectionId: string, actionId: string) {
  const inspection = inspectionPath(establishmentId, inspectionId);
  const item = `${inspection}/action-items/${encodeURIComponent(actionId)}`;
  return { done: `${item}/done`, reopen: `${item}/reopen` };
}

export const inspectionsKey = (establishmentId: string) => ["inspections", establishmentId];
export const inspectionKey = (establishmentId: string, id: string) => [
  "inspection",
  establishmentId,
  id,
];

/** The list's query, for `useQuery` and for a `fetchQuery` that needs its answer. */
export const inspectionsQuery = (api: Pick<ApiClient, "get">, establishmentId: string) => ({
  queryKey: inspectionsKey(establishmentId),
  queryFn: () => api.get<InspectionSummary[]>(inspectionsPath(establishmentId)),
});

/** `enabled` (here and below): the user holds `inspections.read`. */
export function useInspections(establishmentId: string, enabled: boolean) {
  const { api } = useSession();
  return useQuery({ ...inspectionsQuery(api, establishmentId), enabled });
}

/** Always for the selected establishment: another one's inspection answers 404. */
export function useInspection(establishmentId: string, id: string | undefined, enabled: boolean) {
  const { api } = useSession();
  return useQuery({
    queryKey: inspectionKey(establishmentId, id ?? ""),
    queryFn: () => api.get<InspectionDetail>(inspectionPath(establishmentId, id ?? "")),
    enabled: enabled && id !== undefined,
  });
}
