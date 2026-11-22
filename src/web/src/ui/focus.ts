import { useEffect, useEffectEvent, useRef, useState } from "react";

/** Focuses the first of `ids` that is on the page. */
export const focusById = (...ids: string[]) =>
  ids
    .map((id) => document.getElementById(id))
    .find((element) => element !== null)
    ?.focus();

/** Nothing has focus: it fell to the page, as it does when the focused element goes away. */
export const focusIsLost = () =>
  document.activeElement === null || document.activeElement === document.body;

/**
 * A control that may hold focus goes away (a permission taken away, Reconnect once it worked):
 * when `present` turns false and focus fell to the page with it, the heading takes it (the first
 * of `headingIds` on the page).
 */
export function useRefocusWhenGone(present: boolean, ...headingIds: string[]) {
  const wasPresent = useRef(present);
  const focusHeading = useEffectEvent(() => focusById(...headingIds));

  useEffect(() => {
    if (wasPresent.current && !present && focusIsLost()) focusHeading();
    wasPresent.current = present;
  }, [present]);
}

/**
 * Focus for an element the next commit shows (after a navigation, or a view that replaces this
 * one): the returned function asks for it by id. A new request per call, so the same id twice
 * still moves focus.
 */
export function useFocusAfterCommit() {
  const [request, setRequest] = useState<{ id: string }>();

  useEffect(() => {
    if (request) focusById(request.id);
  }, [request]);

  return (id: string) => setRequest({ id });
}

/**
 * A new `focusKey` from the shell (the drawer chose an establishment, or a retry loaded the list)
 * focuses the page's heading: the first of `headingIds` on the page.
 */
export function useShellFocusRequest(focusKey: number, ...headingIds: string[]) {
  const focusHeading = useEffectEvent(() => focusById(...headingIds));

  useEffect(() => {
    if (focusKey > 0) focusHeading();
  }, [focusKey]);
}
