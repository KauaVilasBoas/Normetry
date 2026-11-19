// Which StatusPill an API status shows as (DESIGN.md Status, Ingestion and Review status pills;
// inspection-screen.md §4). Labels come from copy.ts.

import { documentStatuses, inspectionStatuses, reviewStatuses } from "../api/contract";

export type PillVariant =
  | "waiting"
  | "processing"
  | "done"
  | "attention"
  | "compliant"
  | "nonCompliant"
  | "edited"
  | "rejected";

const variants = {
  /** A document version's ingestion. */
  ingestion: {
    [documentStatuses.uploaded]: "waiting",
    [documentStatuses.processing]: "processing",
    [documentStatuses.indexed]: "done",
    [documentStatuses.failed]: "attention",
  },
  /** An inspection: a process state, so the ingestion styles. */
  inspection: {
    [inspectionStatuses.requested]: "waiting",
    [inspectionStatuses.running]: "processing",
    [inspectionStatuses.completed]: "done",
    [inspectionStatuses.failed]: "attention",
  },
  /** A finding's verdict: a compliance state, so status colors. */
  verdict: {
    compliant: "compliant",
    non_compliant: "nonCompliant",
    insufficient_evidence: "attention",
  },
  /** A finding's review: not compliance, so no green or red. */
  review: {
    [reviewStatuses.pending]: "waiting",
    [reviewStatuses.accepted]: "done",
    [reviewStatuses.edited]: "edited",
    [reviewStatuses.rejected]: "rejected",
  },
} satisfies Record<string, Record<string, PillVariant>>;

/** The pill for `status` of this `kind`; an unknown status looks like Waiting. */
export function pillVariant(kind: keyof typeof variants, status: string): PillVariant {
  return (variants[kind] as Record<string, PillVariant | undefined>)[status] ?? "waiting";
}
