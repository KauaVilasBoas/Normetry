import { QueryCache, QueryClient, useQuery, type UseQueryResult } from "@tanstack/react-query";
import { useSession } from "../auth/session";
import { ApiError } from "./client";

/** The permission catalog the UI knows (the server may add more; unknown ones show as sent). */
export type Permission =
  | "establishments.read"
  | "establishments.manage"
  | "documents.read"
  | "documents.upload"
  | "documents.delete"
  | "chat.ask"
  | "profiles.read"
  | "profiles.manage";

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

/** A copy of the cited passage stored with the answer (ADR 007), so it opens without a fetch. */
export interface Citation {
  id: string;
  sourceTitle: string;
  sourceVersion: string;
  language: string;
  sectionPath: string;
  text: string;
  quote: string;
  /** `text.slice(quoteStart, quoteEnd)` is the quote; both null when it could not be mapped. */
  quoteStart: number | null;
  quoteEnd: number | null;
  officialUrl: string | null;
}

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

/** `enabled` (here and below): the user holds `documents.read`. */
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
