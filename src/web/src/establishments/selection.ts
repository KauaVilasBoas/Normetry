import type { UseQueryResult } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { ApiError } from "../api/client";
import { queryFailed, type Establishment, type Me } from "../api/queries";
import { copy, format } from "../copy";

export type ListState = "loading" | "list" | "empty" | "error" | "forbidden";

/** What the switcher and the establishments card show; both requests must succeed. */
export function listState(
  me: UseQueryResult<Me>,
  list: UseQueryResult<Establishment[]>,
): ListState {
  // 403 from /me: the token has no valid tenant. From /establishments it is a missing permission,
  // which refetches /me (createQueryClient) and so hides the card.
  if (me.error instanceof ApiError && me.error.status === 403) return "forbidden";
  // Kept failed while Try again refetches it, so its button (which holds focus) stays.
  if (queryFailed(me) || queryFailed(list)) return "error";
  if (!me.data || !list.data) return "loading";
  return list.data.length > 0 ? "list" : "empty";
}

/** An establishment's type, jurisdiction and regulation; unknown enum values show as sent. */
export function describeEstablishment({ type, jurisdiction }: Establishment) {
  const typeLabel = copy.establishmentTypes[type] ?? type;
  const place = copy.jurisdictions[jurisdiction];
  return place
    ? format(copy.picker.meta, {
        type: typeLabel,
        jurisdiction: place.name,
        regulation: place.regulation,
      })
    : format(copy.picker.metaUnknownJurisdiction, { type: typeLabel, jurisdiction });
}

// Only the id is remembered, per tenant and user, as a convenience: the core reloads it under RLS.
// The jurisdiction is never stored or sent for use in requests.
const keyPrefix = "normetry.establishment.";
const storageKey = (me: Me) => `${keyPrefix}${me.tenantId}.${me.userId}`;

/** Sign-out forgets every remembered establishment in this browser. */
export function forgetEstablishments() {
  try {
    Object.keys(localStorage)
      .filter((key) => key.startsWith(keyPrefix))
      .forEach((key) => localStorage.removeItem(key));
  } catch {
    // Storage blocked: nothing was remembered.
  }
}

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
 * still in the list, else the first. Undefined only while the list is loading, empty or failed.
 */
export function useEstablishmentSelection(me: Me | undefined, list: Establishment[] | undefined) {
  const [chosenId, setChosenId] = useState<string>();
  const key = me && storageKey(me);
  const stored = key ? readStored(key) : null;

  const selected = list?.find((item) => item.id === (chosenId ?? stored)) ?? list?.[0];

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
