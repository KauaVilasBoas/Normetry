import { QueryCache, QueryClient, useQuery, type UseQueryResult } from "@tanstack/react-query";
import { useSession } from "../auth/session";
import { ApiError, type ApiClient } from "./client";
import { permissions, sourceTypes } from "./contract";

type Catalog = typeof permissions;

/** The permission catalog the UI knows (the server may add more; unknown ones show as sent). */
export type Permission = {
  [Area in keyof Catalog]: Catalog[Area][keyof Catalog[Area]];
}[keyof Catalog];

/** A profile as other resources show it. `systemKey` names a system profile; null for custom. */
export interface ProfileSummary {
  id: string;
  name: string;
  systemKey: "owner" | "manager" | "auditor" | null;
}

/** GET /me. `tenantName` is null for a tenant the core has no row for. */
export interface Me {
  userId: string;
  tenantId: string;
  tenantName?: string | null;
  /** What the user's profiles grant. Hides what they cannot do; the server checks every call. */
  permissions: string[];
  profiles: ProfileSummary[];
}

/** Whether the user holds a permission; false while /me is unknown, so nothing gated flashes. */
export const can = (me: Me | undefined, permission: Permission) =>
  me?.permissions.includes(permission) ?? false;

/** GET /establishments: an unpaginated list, already sorted by name. Enums travel by name. */
export interface Establishment {
  id: string;
  name: string;
  type: string;
  jurisdiction: string;
}

export const meKey = ["me"];

export function createQueryClient() {
  const client: QueryClient = new QueryClient({
    // A 403 elsewhere means a permission was taken away mid-session: /me again, which hides what
    // the user can no longer do. Only a 403 from /me itself means the token has no tenant.
    queryCache: new QueryCache({
      onError: (error, query) => {
        if (error instanceof ApiError && error.status === 403 && query.queryKey[0] !== meKey[0])
          void client.invalidateQueries({ queryKey: meKey });
      },
    }),
    defaultOptions: {
      queries: {
        // 4xx answers do not change on retry (401 already went through the session flow).
        retry: (failures, error) =>
          !(error instanceof ApiError && error.status < 500) && failures < 1,
        refetchOnWindowFocus: false,
      },
    },
  });
  return client;
}

export function useMe() {
  const { api } = useSession();
  return useQuery({ queryKey: meKey, queryFn: () => api.get<Me>("/me") });
}

/** `enabled`: the user holds `establishments.read` (without it the list is never requested). */
export function useEstablishments(enabled = true) {
  const { api } = useSession();
  return useQuery({
    queryKey: ["establishments"],
    queryFn: () => api.get<Establishment[]>("/establishments"),
    enabled,
  });
}

/** GET /chat/quota (core). `limit` and `remaining` are null on unlimited plans. */
export interface ChatQuota {
  limit: number | null;
  remaining: number | null;
  resetsAt: string;
}

/** A conversation as the AI service lists it; `title` is the first question, null before one. */
export interface ConversationSummary {
  id: string;
  establishmentId: string;
  title: string | null;
  updatedAt: string;
}

interface CitedPassage {
  id: string;
  language: string;
  sectionPath: string;
  text: string;
  quote: string;
  /** `text.slice(quoteStart, quoteEnd)` is the quote; both null when it could not be mapped. */
  quoteStart: number | null;
  quoteEnd: number | null;
}

/**
 * A copy of the cited passage stored with the answer (ADR 007), so it opens without a fetch: the
 * regulation's, or one of the establishment's own documents (evidence, never a rule).
 */
export type Citation =
  | (CitedPassage & {
      sourceType: typeof sourceTypes.regulation;
      sourceTitle: string;
      sourceVersion: string;
      officialUrl: string | null;
    })
  | (CitedPassage & {
      sourceType: typeof sourceTypes.document;
      documentId: string;
      /** Null on documents uploaded before titles reached the AI service. */
      documentTitle: string | null;
      /** Sop, License, Log, Report, Other, or null. */
      documentType: string | null;
      documentVersion: number;
    });

/** The quote's offsets map onto `text`, so it can be highlighted there. */
export const quoteFound = ({
  text,
  quoteStart: start,
  quoteEnd: end,
}: Pick<Citation, "text" | "quoteStart" | "quoteEnd">) =>
  start !== null && end !== null && start >= 0 && start < end && end <= text.length;

export interface Claim {
  text: string;
  citations: Citation[];
}

export type ConversationMessage =
  | { id: string; role: "user"; text: string; createdAt: string }
  | {
      id: string;
      role: "assistant";
      kind: "answer" | "refusal" | "unavailable";
      language: string | null;
      claims: unknown[];
      createdAt: string;
    };

/** GET /ai/conversations/{id}: the summary plus its messages, oldest first. */
export interface Conversation extends ConversationSummary {
  messages: ConversationMessage[];
}

export const conversationsKey = ["conversations"];
export const quotaKey = ["chat-quota"];

/** The caller's conversations in every establishment; the screen filters by establishment. */
export function useConversations(enabled = true) {
  const { api } = useSession();
  return useQuery({
    queryKey: conversationsKey,
    queryFn: () => api.get<ConversationSummary[]>("/ai/conversations"),
    enabled,
  });
}

/**
 * One conversation. Fetched once per opening: turns asked in the open view are appended to it
 * locally, so a refetch would repeat them. Dropped when the view closes, so reopening refetches.
 */
export function useConversation(id: string | undefined) {
  const { api } = useSession();
  return useQuery({
    queryKey: ["conversation", id],
    queryFn: () => api.get<Conversation>(`/ai/conversations/${encodeURIComponent(id ?? "")}`),
    enabled: id !== undefined,
    staleTime: Infinity,
    gcTime: 0,
  });
}

/** What is left today. A failure only hides the counter, so it is not retried. */
export function useChatQuota() {
  const { api } = useSession();
  return useQuery({
    queryKey: quotaKey,
    queryFn: () => api.get<ChatQuota>("/chat/quota"),
    retry: false,
  });
}

/**
 * A document version. `status` is Uploaded (shown as Waiting), Processing, Indexed or Failed;
 * `failureCode` one of the queue contract's reason codes or `timed_out`. Unknown values show as
 * sent (statuses) or with the generic reason (codes).
 */
export interface DocumentVersion {
  id: string;
  number: number;
  fileName: string;
  contentType: string;
  size: number;
  status: string;
  failureCode: string | null;
  failureReason: string | null;
  uploadedBy: { id: string; displayName: string | null };
  uploadedAt: string;
  statusChangedAt: string;
  chunkCount: number | null;
  pageCount: number | null;
  language: string | null;
}

interface DocumentFields {
  id: string;
  establishmentId: string;
  title: string;
  /** Sop, License, Log, Report or Other. */
  type: string;
  createdAt: string;
  updatedAt: string;
  /** The newest Indexed version; null before one is. */
  currentVersionNumber: number | null;
  /** The last valid day, a date with no time ("2026-11-23"); null when it doesn't expire. */
  expiresOn: string | null;
}

/** A row of GET /establishments/{id}/documents (newest updated first). */
export interface DocumentSummary extends DocumentFields {
  latestVersion: DocumentVersion;
}

/** GET /documents/{id}, and what an upload answers: every version, newest first. */
export interface DocumentDetail extends DocumentFields {
  versions: DocumentVersion[];
}

export const documentsKey = (establishmentId: string) => ["documents", establishmentId];
export const documentKey = (id: string) => ["document", id];

/**
 * `enabled` (here and below): the user holds `documents.read`. The expiry filter runs in the
 * browser with the pill's local-date rule; the API's `expired` and `expiringWithinDays` count
 * today in UTC and stay for other callers.
 */
export function useDocuments(establishmentId: string, enabled: boolean) {
  const { api } = useSession();
  return useQuery({
    queryKey: documentsKey(establishmentId),
    queryFn: () =>
      api.get<DocumentSummary[]>(
        `/establishments/${encodeURIComponent(establishmentId)}/documents`,
      ),
    enabled,
  });
}

export function useDocument(id: string | undefined, enabled: boolean) {
  const { api } = useSession();
  return useQuery({
    queryKey: documentKey(id ?? ""),
    queryFn: () => api.get<DocumentDetail>(`/documents/${encodeURIComponent(id ?? "")}`),
    enabled: enabled && id !== undefined,
  });
}

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
export interface RegulationCitation {
  sourceTitle: string;
  sourceVersion: string;
  sectionPath: string;
  language: string;
  text: string;
  quote: string;
  quoteStart: number | null;
  quoteEnd: number | null;
}

/** A copy of a passage of the establishment's own document. The title is null on old uploads. */
export interface EvidenceCitation {
  documentId: string;
  documentTitle: string | null;
  documentType: string | null;
  versionNumber: number;
  sectionPath: string;
  language: string;
  text: string;
  quote: string;
  quoteStart: number | null;
  quoteEnd: number | null;
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

const inspectionsPath = (establishmentId: string) =>
  `/establishments/${encodeURIComponent(establishmentId)}/inspections`;

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
    queryFn: () =>
      api.get<InspectionDetail>(
        `${inspectionsPath(establishmentId)}/${encodeURIComponent(id ?? "")}`,
      ),
    enabled: enabled && id !== undefined,
  });
}

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
    queryFn: () =>
      api.get<Overview>(`/establishments/${encodeURIComponent(establishmentId)}/overview`),
  });
}

/** GET /profiles. `permissionCount` is the whole catalog for Owner (`grantsAll`). */
export interface ProfileListItem extends ProfileSummary {
  grantsAll: boolean;
  permissionCount: number;
}

/** GET /profiles/{id}: what it grants, the whole catalog for Owner. */
export interface Profile extends ProfileSummary {
  grantsAll: boolean;
  permissions: string[];
}

/** GET /users: the tenant's users, each after their first sign-in. */
export interface TenantUser {
  id: string;
  displayName: string | null;
  email: string | null;
  isCurrentUser: boolean;
  profiles: ProfileSummary[];
}

export const profilesKey = ["profiles"];
export const profileKey = (id: string) => ["profile", id];
export const usersKey = ["users"];

/** A failed query, kept failed while Try again refetches it (it goes back to pending then). */
export const queryFailed = (query: UseQueryResult) =>
  query.isError || (query.isPending && query.errorUpdateCount > 0);

/** `enabled` (here and below): the user holds `profiles.read`. */
export function useProfiles(enabled: boolean) {
  const { api } = useSession();
  return useQuery({
    queryKey: profilesKey,
    queryFn: () => api.get<ProfileListItem[]>("/profiles"),
    enabled,
  });
}

export function useProfile(id: string | undefined, enabled: boolean) {
  const { api } = useSession();
  return useQuery({
    queryKey: profileKey(id ?? ""),
    queryFn: () => api.get<Profile>(`/profiles/${encodeURIComponent(id ?? "")}`),
    enabled: enabled && id !== undefined,
  });
}

/** The permission names the server knows, for the profile editor. */
export function usePermissionCatalog(enabled: boolean) {
  const { api } = useSession();
  return useQuery({
    queryKey: ["permissions"],
    queryFn: async () =>
      (await api.get<{ name: string }[]>("/permissions")).map((permission) => permission.name),
    enabled,
    staleTime: Infinity,
  });
}

export function useUsers(enabled: boolean) {
  const { api } = useSession();
  return useQuery({
    queryKey: usersKey,
    queryFn: () => api.get<TenantUser[]>("/users"),
    enabled,
  });
}
