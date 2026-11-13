import type { UseQueryResult } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { ApiError } from "../api/client";
import type { Establishment, Me } from "../api/queries";
import { copy, format } from "../copy";

export type ListState = "loading" | "list" | "empty" | "error" | "forbidden";

/** What the picker and the switcher show; both requests must succeed. */
export function listState(
  me: UseQueryResult<Me>,
  list: UseQueryResult<Establishment[]>,
): ListState {
  // 403: the token has no valid tenant, so every tenant-scoped endpoint refuses it.
  if ([me.error, list.error].some((error) => error instanceof ApiError && error.status === 403))
    return "forbidden";
  if (me.isError || list.isError) return "error";
  if (!me.data || !list.data) return "loading";
  return list.data.length > 0 ? "list" : "empty";
}

/** Labels for an establishment; unknown enum values show as sent, never crash. */
export function describeEstablishment({ name, type, jurisdiction }: Establishment) {
  const typeLabel = copy.establishmentTypes[type] ?? type;
  const place = copy.jurisdictions[jurisdiction];
  const jurisdictionLabel = place?.name ?? jurisdiction;
  return {
    meta: place
      ? format(copy.picker.meta, {
          type: typeLabel,
          jurisdiction: place.name,
          regulation: place.regulation,
        })
      : format(copy.picker.metaUnknownJurisdiction, { type: typeLabel, jurisdiction }),
    label: format(copy.picker.optionLabel, {
      name,
      type: typeLabel,
      jurisdiction: jurisdictionLabel,
    }),
  };
}

// Only the id is remembered, per tenant and user, as a convenience: the core reloads it under RLS.
// The jurisdiction is never stored or sent for use in requests.
const storageKey = (me: Me) => `normetry.establishment.${me.tenantId}.${me.userId}`;

function readStored(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStored(key: string, id: string | null) {
  try {
    if (id === null) localStorage.removeItem(key);
    else localStorage.setItem(key, id);
  } catch {
    // Storage blocked: the choice lasts for this page only.
  }
}

/**
 * The selected establishment: the user's choice in this page, else the remembered one if it is
 * still in the list, else the only one. Undefined means the picker shows.
 */
export function useEstablishmentSelection(me: Me | undefined, list: Establishment[] | undefined) {
  const [chosenId, setChosenId] = useState<string>();
  const key = me && storageKey(me);
  const stored = key ? readStored(key) : null;

  const selected =
    list?.find((item) => item.id === (chosenId ?? stored)) ??
    (chosenId === undefined && list?.length === 1 ? list[0] : undefined);

  const staleStored = !!list && stored !== null && !list.some((item) => item.id === stored);
  useEffect(() => {
    if (key && staleStored) writeStored(key, null);
  }, [key, staleStored]);

  const select = (establishment: Establishment) => {
    setChosenId(establishment.id);
    if (key) writeStored(key, establishment.id);
  };

  return { selected, select };
}
