import { WifiOff } from "lucide-react";
import { copy } from "../copy";
import { Button, Notice, Spinner } from "../ui/primitives";
import { versionText } from "./documents";
import type { LiveState } from "./live";

// Pieces the list and the detail share.

export const LIST_TITLE_ID = "documents-title";
export const DETAIL_TITLE_ID = "document-title";

export interface Live {
  state: LiveState;
  reconnect: () => void;
}

/** A table header cell (DESIGN.md Table, on the inset header row). */
export const th = "h-9 px-3 text-left text-caption font-medium text-text-muted";

/** Shown while live updates are paused or stopped, under a card's header. */
export function ConnectionNotice({ live }: { live: Live }) {
  if (live.state === "live") return null;
  const reconnecting = live.state === "reconnecting";
  const retrying = live.state === "retrying";
  return (
    <Notice
      role="status"
      className="mx-5 mb-4"
      icon={reconnecting ? <Spinner /> : <WifiOff aria-hidden="true" className="size-4" />}
      title={reconnecting ? copy.documents.reconnecting : copy.documents.stopped}
      action={
        !reconnecting && (
          // aria-disabled, not disabled, while it reconnects: focus stays on the button.
          <Button
            variant="secondary"
            aria-disabled={retrying}
            onClick={retrying ? undefined : live.reconnect}
          >
            {retrying && <Spinner />}
            {copy.documents.reconnect}
          </Button>
        )
      }
    />
  );
}

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
