import { useQuery } from "@tanstack/react-query";
import { useSession } from "../auth/session";
import type { InspectionSummary } from "./inspections";
import { establishmentPath } from "./queries";

/** A theme's verdict counts in the latest inspection, Rejected findings left out. */
export interface ThemeVerdicts extends VerdictCounts {
  theme: string;
}

export type VerdictCounts = InspectionSummary["verdictCounts"];

/**
 * The latest Completed inspection's results, by each finding's current verdict, Rejected findings
 * left out. `compliance` is compliant ÷ the rest (0 to 1), null with no finding left. `byTheme`
 * keeps the checklist order; `topRiskThemes` are at most 3, highest score first.
 */
export interface LatestInspection {
  id: string;
  completedAt: string;
  regulation: string;
  compliance: number | null;
  verdictCounts: VerdictCounts;
  byTheme: ThemeVerdicts[];
  topRiskThemes: { theme: string; score: number }[];
}

export interface CompliancePoint {
  inspectionId: string;
  completedAt: string;
  compliance: number | null;
}

/** `type` is Sop, License, Log, Report or Other; `expiresOn` a date with no time. */
export interface ExpiringDocument {
  id: string;
  title: string;
  type: string;
  expiresOn: string;
}

/**
 * GET /establishments/{e}/overview. The inspection sections (`latestInspection` to `series`) are
 * there only with `inspections.read`, `documents` only with `documents.read`. The counts belong to
 * the latest inspection; `series` holds the last Completed ones, oldest first.
 */
export interface Overview {
  latestInspection?: LatestInspection | null;
  openActionItems?: number;
  overdueActionItems?: number;
  pendingReviews?: number;
  series?: CompliancePoint[];
  documents?: { expired: number; expiringSoon: number; list: ExpiringDocument[] };
}

/**
 * The key holds both permissions, so a change mid-session refetches. Refetched on mount, so
 * coming back from a review or an upload shows current numbers.
 */
export function useOverview(
  establishmentId: string,
  inspectionsRead: boolean,
  documentsRead: boolean,
) {
  const { api } = useSession();
  return useQuery({
    queryKey: ["overview", establishmentId, inspectionsRead, documentsRead],
    queryFn: () => api.get<Overview>(`${establishmentPath(establishmentId)}/overview`),
  });
}
