import { useQuery } from "@tanstack/react-query";
import { useSession } from "../auth/session";

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
