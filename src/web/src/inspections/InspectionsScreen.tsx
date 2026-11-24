import { useEffect } from "react";
import { useNavigate } from "react-router";
import { offersReconnect } from "../api/live";
import type { Establishment, Me } from "../api/queries";
import { copy } from "../copy";
import { paths, useArrivalKey } from "../routes";
import { useAnnouncer } from "../ui/announcer";
import {
  focusById,
  useFocusAfterCommit,
  useRefocusWhenGone,
  useShellFocusRequest,
} from "../ui/focus";
import { PoliteRegion } from "../ui/primitives";
import { InspectionDetailView } from "./InspectionDetail";
import { DETAIL_TITLE_ID, LIST_TITLE_ID, useInspectionLiveUpdates } from "./inspections";
import { InspectionList } from "./InspectionList";

/**
 * Simulated Inspection (spec inspection-screen.md): `/inspections` is the selected
 * establishment's history, `/inspections/:inspectionId` one inspection. Above both: the live
 * connection and the polite region, so a message survives the move between them. A new
 * `focusKey` (the drawer chose an establishment, or a retry loaded the list) moves focus to the
 * page's heading.
 */
export function InspectionsScreen({
  establishment,
  inspectionId,
  me,
  focusKey,
}: {
  establishment: Establishment;
  inspectionId: string | undefined;
  me: Me | undefined;
  focusKey: number;
}) {
  const navigate = useNavigate();
  const { announcements, announce } = useAnnouncer();

  const focus = useFocusAfterCommit();
  useShellFocusRequest(focusKey, LIST_TITLE_ID, DETAIL_TITLE_ID);

  // A link from another screen ("Go to Simulated Inspection", "View inspection", a link into the
  // findings) asks for the page's heading with its state. The history's is there at once; the
  // detail focuses its own once the inspection has loaded.
  const arrival = useArrivalKey();
  useEffect(() => {
    if (arrival && inspectionId === undefined) focusById(LIST_TITLE_ID);
  }, [arrival, inspectionId]);

  const live = useInspectionLiveUpdates({
    establishmentId: establishment.id,
    inspectionId,
    announce,
  });

  // Reconnect goes away once it worked; focus moves to the heading instead of the page.
  useRefocusWhenGone(offersReconnect(live.state), LIST_TITLE_ID, DETAIL_TITLE_ID);

  return (
    <>
      {inspectionId === undefined ? (
        <InspectionList
          key={establishment.id}
          establishment={establishment}
          me={me}
          live={live}
          onOpen={() => focus(DETAIL_TITLE_ID)}
          onStarted={(id) => {
            void navigate(paths.inspection(id));
            focus(DETAIL_TITLE_ID);
            announce(copy.inspections.started);
          }}
        />
      ) : (
        <InspectionDetailView
          key={inspectionId}
          id={inspectionId}
          establishment={establishment}
          me={me}
          live={live}
          arrival={arrival}
          onBack={() => focus(LIST_TITLE_ID)}
        />
      )}
      <PoliteRegion announcements={announcements} />
    </>
  );
}
