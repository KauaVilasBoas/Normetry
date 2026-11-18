import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { useLiveStatus } from "../api/live";
import {
  inspectionKey,
  inspectionsKey,
  inspectionsQuery,
  type Establishment,
  type InspectionDetail,
  type InspectionSummary,
  type Me,
} from "../api/queries";
import { useSession } from "../auth/session";
import { copy, format } from "../copy";
import { InspectionDetailView } from "./InspectionDetail";
import {
  DETAIL_TITLE_ID,
  failureText,
  findingCount,
  HUB_EVENT,
  HUB_URL,
  inFlight,
  LIST_TITLE_ID,
  milestone,
  milestoneText,
  type InspectionChange,
} from "./inspections";
import { InspectionList } from "./InspectionList";

const focusById = (id: string) => document.getElementById(id)?.focus();

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
  const { api } = useSession();
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

  const listKey = inspectionsKey(establishment.id);
  // The milestone the open inspection last reached, so each is announced once.
  const reached = useRef<{ id: string; milestone: number }>(undefined);

  const changed = (change: InspectionChange) => {
    const id = change.inspectionId;
    const detailKey = inspectionKey(establishment.id, id);
    const listed = queryClient.getQueryData<InspectionSummary[]>(listKey);
    const row = listed?.find((r) => r.id === id);
    const open = queryClient.getQueryData<InspectionDetail>(detailKey);
    const before = row ?? open;
    const { status, itemsEvaluated: evaluated, itemsTotal: total, themes } = change;

    // Progress and the start of a run patch what is shown. An end refetches: it brings results.
    if (inFlight(status)) {
      const patch = { status, itemsEvaluated: evaluated, itemsTotal: total };
      queryClient.setQueryData<InspectionSummary[]>(listKey, (list) =>
        list?.map((r) => (r.id === id ? { ...r, ...patch } : r)),
      );
      queryClient.setQueryData<InspectionDetail>(detailKey, (detail) =>
        detail ? { ...detail, ...patch, themes } : detail,
      );
    }

    if (id === inspectionId && status === "Running") {
      const last =
        reached.current?.id === id
          ? reached.current.milestone
          : milestone(before?.itemsEvaluated ?? 0, before?.itemsTotal ?? 0);
      const now = milestone(evaluated, total);
      if (now > last) announce(milestoneText(evaluated, total));
      reached.current = { id, milestone: Math.max(now, last) };
    }

    const statusChanged = before?.status !== status;
    // Progress only: nothing is refetched, unless the list is there without this inspection.
    if (!statusChanged && (row || !listed)) return;
    // The list is fetched (not just invalidated) for the completion's finding count.
    const list = queryClient.fetchQuery(inspectionsQuery(api, establishment.id));
    list.catch(() => {});
    if (!statusChanged) return;
    void queryClient.invalidateQueries({ queryKey: detailKey });
    if (status === "Failed")
      announce(
        format(copy.inspections.failed, { reason: failureText(change.failureReasonCode).short }),
      );
    if (status === "Completed")
      list.then(
        (rows) => {
          const counts = rows.find((r) => r.id === id)?.verdictCounts;
          if (!counts) return;
          const findings = counts.compliant + counts.nonCompliant + counts.insufficientEvidence;
          announce(format(copy.inspections.completed, { findings: findingCount(findings) }));
        },
        () => {},
      );
  };

  // A request already in flight is new enough: it is not restarted.
  const live = useLiveStatus(HUB_URL, HUB_EVENT, establishment.id, changed, () => {
    void queryClient.invalidateQueries({ queryKey: listKey }, { cancelRefetch: false });
    if (inspectionId)
      void queryClient.invalidateQueries(
        { queryKey: inspectionKey(establishment.id, inspectionId) },
        { cancelRefetch: false },
      );
  });

  // Reconnect goes away once it worked; focus moves to the heading instead of the page.
  const wasRetrying = useRef(false);
  useEffect(() => {
    if (wasRetrying.current && live.state === "live" && document.activeElement === document.body)
      focusById(inspectionId === undefined ? LIST_TITLE_ID : DETAIL_TITLE_ID);
    wasRetrying.current = live.state === "retrying";
  }, [live.state, inspectionId]);

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
            void navigate(`/inspections/${id}`);
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
          onBack={() => focus(LIST_TITLE_ID)}
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
