// Which StatusPill an API status shows as (DESIGN.md Status, Ingestion and Review status pills;
// inspection-screen.md §4). Labels come from copy.ts.

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
    Uploaded: "waiting",
    Processing: "processing",
    Indexed: "done",
    Failed: "attention",
  },
  /** An inspection: a process state, so the ingestion styles. */
  inspection: {
    Requested: "waiting",
    Running: "processing",
    Completed: "done",
    Failed: "attention",
  },
  /** A finding's verdict: a compliance state, so status colors. */
  verdict: {
    compliant: "compliant",
    non_compliant: "nonCompliant",
    insufficient_evidence: "attention",
  },
  /** A finding's review: not compliance, so no green or red. */
  review: { Pending: "waiting", Accepted: "done", Edited: "edited", Rejected: "rejected" },
} satisfies Record<string, Record<string, PillVariant>>;

/** The pill for `status` of this `kind`; an unknown status looks like Waiting. */
export function pillVariant(kind: keyof typeof variants, status: string): PillVariant {
  return (variants[kind] as Record<string, PillVariant | undefined>)[status] ?? "waiting";
}
