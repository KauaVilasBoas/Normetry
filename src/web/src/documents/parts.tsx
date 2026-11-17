import { copy } from "../copy";
import { versionText } from "./documents";

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
