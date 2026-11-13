import { Check, ChevronDown, ChevronUp } from "lucide-react";
import { Select } from "radix-ui";
import { useId } from "react";
import type { Establishment } from "../api/queries";
import { copy } from "../copy";
import { describeEstablishment, type ListState } from "../establishments/selection";
import { Skeleton, TypeIcon } from "../ui/primitives";

const scrollButton = "flex h-6 cursor-default items-center justify-center text-text-secondary";

const trigger =
  "flex h-9 w-full items-center gap-2 rounded-control border border-border px-3 text-left text-body";

/** Sidebar control to change the establishment; a listbox popover (select-only combobox). */
export function EstablishmentSwitcher({
  state,
  establishments,
  selected,
  onSelect,
}: {
  state: ListState;
  establishments: Establishment[] | undefined;
  selected: Establishment | undefined;
  onSelect: (establishment: Establishment) => void;
}) {
  const metaId = useId();
  if (state === "loading") {
    return (
      <button
        type="button"
        aria-disabled="true"
        aria-label={copy.switcher.loading}
        className={trigger}
      >
        <Skeleton className="h-3 flex-1" />
      </button>
    );
  }

  if (state !== "list" || !establishments) {
    return (
      <button type="button" disabled className={`${trigger} text-text-secondary`}>
        <span className="flex-1 truncate">{copy.switcher.none}</span>
        <ChevronDown aria-hidden="true" className="size-4 shrink-0" />
      </button>
    );
  }

  return (
    <Select.Root
      value={selected?.id ?? ""}
      onValueChange={(id) => {
        const next = establishments.find((establishment) => establishment.id === id);
        if (next) onSelect(next);
      }}
    >
      <Select.Trigger
        aria-haspopup="listbox"
        // The name stays fixed; the selected establishment (or the placeholder) is its value.
        aria-label={copy.switcher.name}
        className={`${trigger} hover:bg-elevated`}
      >
        {selected && (
          <TypeIcon type={selected.type} className="size-4 shrink-0 text-text-secondary" />
        )}
        <span
          className={`flex-1 truncate font-medium ${selected ? "text-text-primary" : "text-text-secondary"}`}
        >
          {selected?.name ?? copy.switcher.placeholder}
        </span>
        <ChevronDown aria-hidden="true" className="size-4 shrink-0 text-text-secondary" />
      </Select.Trigger>
      <Select.Portal>
        <Select.Content
          position="popper"
          side="bottom"
          align="start"
          sideOffset={4}
          collisionPadding={16}
          className="z-50 max-h-80 min-w-65 max-w-[calc(100vw-2rem)] overflow-hidden rounded-control border border-border bg-elevated"
        >
          <Select.ScrollUpButton className={scrollButton}>
            <ChevronUp aria-hidden="true" className="size-4" />
          </Select.ScrollUpButton>
          <Select.Viewport className="p-1">
            {establishments.map((establishment) => (
              <Select.Item
                key={establishment.id}
                value={establishment.id}
                textValue={establishment.name}
                // Radix marks only the focused selected option; the spec wants the selected one.
                aria-selected={establishment.id === selected?.id}
                aria-describedby={`${metaId}-${establishment.id}`}
                className="flex min-h-12 cursor-default items-center gap-3 rounded-sm px-3 py-1.5 select-none focus-visible:-outline-offset-2 data-highlighted:bg-inset"
              >
                <span className="min-w-0 flex-1">
                  <Select.ItemText>
                    <span className="block truncate text-body font-medium">
                      {establishment.name}
                    </span>
                  </Select.ItemText>
                  <span
                    id={`${metaId}-${establishment.id}`}
                    className="block text-small text-text-secondary"
                  >
                    {describeEstablishment(establishment).meta}
                  </span>
                </span>
                <Select.ItemIndicator>
                  <Check aria-hidden="true" className="size-4 text-mint" />
                </Select.ItemIndicator>
              </Select.Item>
            ))}
          </Select.Viewport>
          <Select.ScrollDownButton className={scrollButton}>
            <ChevronDown aria-hidden="true" className="size-4" />
          </Select.ScrollDownButton>
        </Select.Content>
      </Select.Portal>
    </Select.Root>
  );
}
