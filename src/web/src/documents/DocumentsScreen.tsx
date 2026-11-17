import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { useLiveStatus } from "../api/live";
import {
  documentKey,
  documentsKey,
  type DocumentSummary,
  type Establishment,
  type Me,
} from "../api/queries";
import { copy, format } from "../copy";
import { DocumentDetailView } from "./DocumentDetail";
import { DocumentList } from "./DocumentList";
import { failureText, HUB_EVENT, HUB_URL, type DocumentStatusChange } from "./documents";
import { DETAIL_TITLE_ID, LIST_TITLE_ID } from "./parts";

const focusById = (id: string) => document.getElementById(id)?.focus();

/**
 * Documents (spec documents-screen.md): `/documents` is the selected establishment's list,
 * `/documents/:documentId` one document. Above both: the live status connection and the polite
 * region, so a message survives the move from the detail back to the list. A new `focusKey` (the
 * drawer chose an establishment, or a retry loaded the list) moves focus to the page's heading.
 */
export function DocumentsScreen({
  establishment,
  documentId,
  me,
  focusKey,
}: {
  establishment: Establishment;
  documentId: string | undefined;
  me: Me | undefined;
  focusKey: number;
}) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [announcements, setAnnouncements] = useState<{ id: number; text: string }[]>([]);
  const announce = (text: string) =>
    setAnnouncements((list) => [...list.slice(-9), { id: (list.at(-1)?.id ?? 0) + 1, text }]);

  const [focusRequest, setFocusRequest] = useState<{ id: string }>();
  useEffect(() => {
    if (focusRequest) focusById(focusRequest.id);
  }, [focusRequest]);
  const focus = (id: string) => setFocusRequest({ id });

  useEffect(() => {
    if (focusKey > 0)
      (document.getElementById(LIST_TITLE_ID) ?? document.getElementById(DETAIL_TITLE_ID))?.focus();
  }, [focusKey]);

  const refetch = (id: string | undefined, options = {}) => {
    void queryClient.invalidateQueries({ queryKey: documentsKey(establishment.id) }, options);
    if (id) void queryClient.invalidateQueries({ queryKey: documentKey(id) }, options);
  };

  // Only the end of processing is announced: Indexed or Failed.
  const changed = (change: DocumentStatusChange) => {
    refetch(change.documentId);
    if (change.status !== "Indexed" && change.status !== "Failed") return;
    const indexed = change.status === "Indexed";
    const reason = failureText(change.failureCode).short;
    if (change.documentId === documentId) {
      const n = String(change.versionNumber);
      announce(
        format(indexed ? copy.documents.versionIndexed : copy.documents.versionFailed, {
          n,
          reason,
        }),
      );
      return;
    }
    const title = queryClient
      .getQueryData<DocumentSummary[]>(documentsKey(establishment.id))
      ?.find((item) => item.id === change.documentId)?.title;
    if (title)
      announce(format(indexed ? copy.documents.indexed : copy.documents.failed, { title, reason }));
  };

  // A request already in flight is new enough: it is not restarted.
  const live = useLiveStatus(HUB_URL, HUB_EVENT, establishment.id, changed, () =>
    refetch(documentId, { cancelRefetch: false }),
  );

  // Reconnect goes away once it worked; focus moves to the heading instead of the page.
  const wasRetrying = useRef(false);
  useEffect(() => {
    if (wasRetrying.current && live.state === "live" && document.activeElement === document.body)
      focusById(documentId === undefined ? LIST_TITLE_ID : DETAIL_TITLE_ID);
    wasRetrying.current = live.state === "retrying";
  }, [live.state, documentId]);

  return (
    <>
      {documentId === undefined ? (
        <DocumentList
          establishment={establishment}
          me={me}
          live={live}
          onOpen={() => focus(DETAIL_TITLE_ID)}
          announce={announce}
        />
      ) : (
        <DocumentDetailView
          key={documentId}
          id={documentId}
          establishment={establishment}
          me={me}
          live={live}
          onBack={() => focus(LIST_TITLE_ID)}
          onDeleted={(title) => {
            void navigate("/documents");
            focus(LIST_TITLE_ID);
            announce(format(copy.documents.deleted, { title }));
          }}
          announce={announce}
        />
      )}
      <div aria-live="polite" className="sr-only">
        {announcements.map((item) => (
          <p key={item.id}>{item.text}</p>
        ))}
      </div>
    </>
  );
}
