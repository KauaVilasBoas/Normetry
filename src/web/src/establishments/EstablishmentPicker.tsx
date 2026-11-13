import { ChevronRight, CircleAlert, Store } from "lucide-react";
import { useId, type ReactNode } from "react";
import type { Establishment } from "../api/queries";
import { copy, format } from "../copy";
import { Button, Skeleton, TypeTile } from "../ui/primitives";
import { describeEstablishment, type ListState } from "./selection";

interface PickerProps {
  state: ListState;
  establishments: Establishment[] | undefined;
  givenName: string | undefined;
  onSelect: (establishment: Establishment) => void;
  onRetry: () => void;
}

/** Shown in main until an establishment is selected. */
export function EstablishmentPicker({
  state,
  establishments,
  givenName,
  onSelect,
  onRetry,
}: PickerProps) {
  return (
    <div className="flex flex-col">
      <p className="text-greeting font-semibold">
        {givenName ? format(copy.picker.greeting, { name: givenName }) : copy.picker.greetingNoName}
      </p>
      <p className="mt-1 text-body text-text-secondary">{copy.picker.subtitle}</p>
      <section
        aria-labelledby="establishments-title"
        aria-busy={state === "loading"}
        className="mt-4 w-full max-w-160 overflow-hidden rounded-card border border-border bg-card p-5 motion-safe:animate-fade-in"
      >
        <h2 id="establishments-title" className="text-title font-semibold">
          {copy.picker.title}
        </h2>
        {state === "loading" && <LoadingRows />}
        {state === "list" && establishments && (
          <ul className="-mx-5 -mb-5 mt-4 divide-y divide-border border-t border-border">
            {establishments.map((establishment) => (
              <li key={establishment.id}>
                <EstablishmentRow establishment={establishment} onSelect={onSelect} />
              </li>
            ))}
          </ul>
        )}
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
            <Button variant="secondary" className="mt-4" onClick={onRetry}>
              {copy.picker.tryAgain}
            </Button>
          </div>
        )}
        {state === "forbidden" && (
          <Message title={copy.picker.noTenantTitle} body={copy.picker.noTenantBody} />
        )}
      </section>
    </div>
  );
}

function EstablishmentRow({
  establishment,
  onSelect,
}: {
  establishment: Establishment;
  onSelect: (establishment: Establishment) => void;
}) {
  const { meta, label } = describeEstablishment(establishment);
  const metaId = useId();
  return (
    <button
      type="button"
      aria-label={label}
      aria-describedby={metaId}
      onClick={() => onSelect(establishment)}
      className="flex min-h-15 w-full items-center gap-3 px-5 py-2 text-left hover:bg-elevated focus-visible:-outline-offset-2"
    >
      <TypeTile type={establishment.type} />
      <span className="min-w-0 flex-1">
        <span className="block truncate text-body font-medium">{establishment.name}</span>
        <span id={metaId} className="block text-small text-text-secondary">
          {meta}
        </span>
      </span>
      <ChevronRight aria-hidden="true" className="size-4 shrink-0 text-text-secondary" />
    </button>
  );
}

function LoadingRows() {
  return (
    <>
      <span className="sr-only">{copy.picker.loading}</span>
      <ul
        aria-hidden="true"
        className="-mx-5 -mb-5 mt-4 divide-y divide-border border-t border-border"
      >
        {[0, 1, 2].map((row) => (
          <li key={row} className="flex min-h-15 items-center gap-3 px-5 py-2">
            <Skeleton className="size-7" />
            <span className="flex flex-1 flex-col gap-2">
              <Skeleton className="h-3 w-2/5" />
              <Skeleton className="h-2.5 w-3/5" />
            </span>
          </li>
        ))}
      </ul>
    </>
  );
}

function Message({ icon, title, body }: { icon?: ReactNode; title: string; body: string }) {
  return (
    <div className="mt-4 flex flex-col items-start gap-2">
      {icon}
      <h3 className="text-title font-semibold">{title}</h3>
      <p className="text-body text-text-secondary">{body}</p>
    </div>
  );
}
