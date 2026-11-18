import { copy } from "../copy";
import { StatusPill, TimeBadge } from "../ui/primitives";
import { expiry, versionText } from "./documents";

// Pieces the list and the detail share.

export const LIST_TITLE_ID = "documents-title";
export const DETAIL_TITLE_ID = "document-title";

/** "v3" (or "—") for the eye, "Version 3" (or "No indexed version yet") for the ear. */
export function VersionText({ number }: { number: number | null }) {
  const text = number === null ? undefined : versionText(number);
  return (
    <>
      <span aria-hidden="true">{text?.short ?? "—"}</span>
      <span className="sr-only">{text?.long ?? copy.documents.noVersion}</span>
    </>
  );
}

/** The Attention pill "Expired", the Time badge "Expires in 10 days", or nothing for a later date. */
export function ExpiryBadge({ expiresOn }: { expiresOn: string }) {
  const { state, label } = expiry(expiresOn);
  if (state === "expired") return <StatusPill variant="attention" label={label} />;
  if (state === "soon") return <TimeBadge dateTime={expiresOn} label={label} />;
  return null;
}
