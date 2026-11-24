import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import { useLocation } from "react-router";
import { inspectionsKey, useInspection, type InspectionSummary } from "../api/inspections";
import { queryFailed, queryNotFound, useQueryRetry } from "../api/queries";
import { hasFindingFilter } from "../routes";
import { focusById, useRefocusWhenGone } from "../ui/focus";
import { DETAIL_TITLE_ID, FINDINGS_TITLE_ID, inFlight } from "./inspections";

/**
 * The inspection the detail view shows: its date (the list's row while it loads), whether it was
 * not found or failed to load, and Try again. When it stops being live and focus was lost with
 * the running card, focus moves to the title.
 */
export function useInspectionDetail({
  id,
  establishmentId,
  arrival,
}: {
  id: string;
  establishmentId: string;
  /** A link from another screen asked for the heading (its location key). */
  arrival: string | undefined;
}) {
  const queryClient = useQueryClient();
  const query = useInspection(establishmentId, id, true);
  const notFound = queryNotFound(query);
  const failed = queryFailed(query) && !notFound;
  const load = useQueryRetry(query, DETAIL_TITLE_ID, failed);
  const detail = query.data;

  useRefocusWhenGone(detail !== undefined && inFlight(detail.status), DETAIL_TITLE_ID);
  useArrivalFocus(arrival, detail !== undefined || notFound || failed);

  return {
    detail,
    requestedAt:
      detail?.requestedAt ??
      queryClient
        .getQueryData<InspectionSummary[]>(inspectionsKey(establishmentId))
        ?.find((row) => row.id === id)?.requestedAt,
    notFound,
    failed,
    retrying: failed && load.retrying,
    retry: load.retry,
  };
}

/**
 * An arrival focuses the title once the view is `settled` (it shows the inspection, or why it
 * can't), or the Findings heading when the link carried a filter (FindingsCard has read it then).
 */
function useArrivalFocus(arrival: string | undefined, settled: boolean) {
  const { search } = useLocation();
  const arrived = useRef<string>(undefined);

  useEffect(() => {
    if (!arrival || arrived.current === arrival || !settled) return;
    arrived.current = arrival;
    if (hasFindingFilter(new URLSearchParams(search)))
      focusById(FINDINGS_TITLE_ID, DETAIL_TITLE_ID);
    else focusById(DETAIL_TITLE_ID);
  }, [arrival, settled, search]);
}
