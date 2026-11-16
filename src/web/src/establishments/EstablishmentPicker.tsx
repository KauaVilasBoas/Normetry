import { CircleAlert, Store } from "lucide-react";
import { copy } from "../copy";
import { Button, Message, Spinner } from "../ui/primitives";
import type { ListState } from "./selection";

interface PickerProps {
  state: ListState;
  onRetry: () => void;
  /** A request is in flight (after Try again). */
  fetching: boolean;
}

/**
 * Main while no establishment can be selected. Loading shows nothing but a busy label (the
 * sidebar switcher's skeleton is the visible cue); an empty or failed list shows the
 * establishments card. Once the list loads, the remembered establishment, else the first, is
 * selected and the switcher changes it.
 */
export function EstablishmentPicker({ state, onRetry, fetching }: PickerProps) {
  if (state === "loading") {
    return (
      <div aria-busy="true">
        <span className="sr-only">{copy.picker.loading}</span>
      </div>
    );
  }
  const retrying = state === "error" && fetching;
  return (
    <section
      aria-labelledby="establishments-title"
      aria-busy={retrying}
      className="w-full max-w-160 overflow-hidden rounded-card border border-border bg-card p-5 motion-safe:animate-fade-in"
    >
      <h2 id="establishments-title" className="text-title font-semibold">
        {copy.picker.title}
      </h2>
      {state === "empty" && (
        <Message
          icon={<Store aria-hidden="true" className="size-5 text-neutral" />}
          title={copy.picker.emptyTitle}
          body={copy.picker.emptyBody}
        />
      )}
      {state === "error" && (
        <div role="alert">
          <Message
            icon={<CircleAlert aria-hidden="true" className="size-5 text-text-secondary" />}
            title={copy.picker.errorTitle}
            body={copy.picker.errorBody}
          />
          {/* aria-disabled, not disabled, while retrying: focus stays on the button. */}
          <Button
            variant="secondary"
            className="mt-4"
            aria-disabled={retrying}
            onClick={retrying ? undefined : onRetry}
          >
            {retrying && <Spinner />}
            {copy.picker.tryAgain}
          </Button>
        </div>
      )}
    </section>
  );
}
