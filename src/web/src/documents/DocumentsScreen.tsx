import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router";
import { documentStatuses } from "../api/contract";
import { documentKey, documentsKey } from "../api/documents";
import { offersReconnect, useLiveStatus } from "../api/live";
import type { Establishment, Me } from "../api/queries";
import { copy, format } from "../copy";
import { paths } from "../routes";
import { useAnnouncer } from "../ui/announcer";
import {
  focusById,
  useFocusAfterCommit,
  useRefocusWhenGone,
  useShellFocusRequest,
} from "../ui/focus";
import { PoliteRegion } from "../ui/primitives";
import { DocumentDetailView } from "./DocumentDetail";
import { DocumentList } from "./DocumentList";
import {
  cachedTitle,
  failureText,
  HUB_EVENT,
  HUB_URL,
  type DocumentStatusChange,
  type ExpiryFilter,
} from "./documents";
import { DETAIL_TITLE_ID, LIST_TITLE_ID } from "./parts";

/**
 * Documents (spec documents-screen.md): `/documents` is the selected establishment's list,
 * `/documents/:documentId` one document. Above both: the live status connection and the polite
 * region, so a message survives the move from the detail back to the list. A new `focusKey` (the
 * drawer chose an establishment, or a retry loaded the list) moves focus to the page's heading.
 * The list's expiry filter lives here too, so it is still set when a document is closed.
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
  const { announcements, announce } = useAnnouncer();
  const [filter, setFilter] = useState<ExpiryFilter>("");

  const focus = useFocusAfterCommit();
  useShellFocusRequest(focusKey, LIST_TITLE_ID, DETAIL_TITLE_ID);

  // A link from another screen ("Go to Documents", a finding's "Open document") asks for the
  // page's heading with its state.
  const location = useLocation();
  const focusArrival =
    (location.state as { focusTitle?: unknown } | null)?.focusTitle === true
      ? location.key
      : undefined;
  useEffect(() => {
    if (focusArrival) focusById(LIST_TITLE_ID, DETAIL_TITLE_ID);
  }, [focusArrival]);

  const refetch = (id: string | undefined, options = {}) => {
    void queryClient.invalidateQueries({ queryKey: documentsKey(establishment.id) }, options);
    if (id) void queryClient.invalidateQueries({ queryKey: documentKey(id) }, options);
  };

  // Only the end of processing is announced: Indexed or Failed.
  const changed = (change: DocumentStatusChange) => {
    refetch(change.documentId);
    if (change.status !== documentStatuses.indexed && change.status !== documentStatuses.failed)
      return;
    const indexed = change.status === documentStatuses.indexed;
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
    const title = cachedTitle(queryClient, establishment.id, change.documentId);
    if (title)
      announce(format(indexed ? copy.documents.indexed : copy.documents.failed, { title, reason }));
  };

  // A request already in flight is new enough: it is not restarted.
  const live = useLiveStatus(HUB_URL, HUB_EVENT, establishment.id, changed, () =>
    refetch(documentId, { cancelRefetch: false }),
  );

  // Reconnect goes away once it worked; focus moves to the heading instead of the page.
  useRefocusWhenGone(offersReconnect(live.state), LIST_TITLE_ID, DETAIL_TITLE_ID);

  return (
    <>
      {documentId === undefined ? (
        <DocumentList
          establishment={establishment}
          me={me}
          live={live}
          filter={filter}
          onFilter={setFilter}
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
            void navigate(paths.documents);
            focus(LIST_TITLE_ID);
            announce(format(copy.documents.deleted, { title }));
          }}
          announce={announce}
        />
      )}
      <PoliteRegion announcements={announcements} />
    </>
  );
}
