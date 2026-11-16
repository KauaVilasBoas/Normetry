import { QueryCache, QueryClient, useQuery } from "@tanstack/react-query";
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
