import { useQuery } from "@tanstack/react-query";
import { useSession } from "../auth/session";
import { sourceTypes } from "./contract";
import type { StoredPassage } from "./passages";

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

/** A passage an answer cites, by its id in the answer. */
interface CitedPassage extends StoredPassage {
  id: string;
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

/** The core's gateway to the AI service. */
const gateway = "/ai";
const conversationsPath = `${gateway}/conversations`;
/** GET one conversation with its messages; DELETE removes it. */
export const conversationPath = (id: string) => `${conversationsPath}/${encodeURIComponent(id)}`;
/** POST starts a conversation in the establishment. */
export const establishmentConversationsPath = (establishmentId: string) =>
  `${gateway}/establishments/${encodeURIComponent(establishmentId)}/conversations`;
/** POST asks a question; the answer streams back. */
export const conversationMessagesPath = (establishmentId: string, conversationId: string) =>
  `${establishmentConversationsPath(establishmentId)}/${encodeURIComponent(conversationId)}/messages`;

export const conversationsKey = ["conversations"];
export const quotaKey = ["chat-quota"];

/** The caller's conversations in every establishment; the screen filters by establishment. */
export function useConversations(enabled = true) {
  const { api } = useSession();
  return useQuery({
    queryKey: conversationsKey,
    queryFn: () => api.get<ConversationSummary[]>(conversationsPath),
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
    queryFn: () => api.get<Conversation>(conversationPath(id ?? "")),
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
