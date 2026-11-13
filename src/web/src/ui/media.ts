import { useSyncExternalStore } from "react";

/** Whether a media query matches now; re-renders when that changes. */
export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (onChange) => {
      const list = window.matchMedia(query);
      list.addEventListener("change", onChange);
      return () => list.removeEventListener("change", onChange);
    },
    () => window.matchMedia(query).matches,
  );
}

/** Breakpoints of the chat layout (DESIGN.md §4). */
export const media = {
  /** The conversation list is inline. */
  list: "(min-width: 768px)",
  /** The source panel is inline. */
  source: "(min-width: 1280px)",
  reducedMotion: "(prefers-reduced-motion: reduce)",
};
