import { QueryClient, useQuery } from "@tanstack/react-query";
import { useSession } from "../auth/session";
import { ApiError } from "./client";

/** GET /me. `tenantName` arrives with #52; until then it is absent. */
export interface Me {
  userId: string;
  tenantId: string;
  tenantName?: string;
  roles: string[];
}

/** GET /establishments: an unpaginated list, already sorted by name. Enums travel by name. */
export interface Establishment {
  id: string;
  name: string;
  type: string;
  jurisdiction: string;
}

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // 4xx answers do not change on retry (401 already went through the session flow).
        retry: (failures, error) =>
          !(error instanceof ApiError && error.status < 500) && failures < 1,
        refetchOnWindowFocus: false,
      },
    },
  });
}

export function useMe() {
  const { api } = useSession();
  return useQuery({ queryKey: ["me"], queryFn: () => api.get<Me>("/me") });
}

export function useEstablishments() {
  const { api } = useSession();
  return useQuery({
    queryKey: ["establishments"],
    queryFn: () => api.get<Establishment[]>("/establishments"),
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
export function useConversations() {
  const { api } = useSession();
  return useQuery({
    queryKey: conversationsKey,
    queryFn: () => api.get<ConversationSummary[]>("/ai/conversations"),
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
