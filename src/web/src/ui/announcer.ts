import { useState } from "react";

/** One line of a polite live region; `lang` when it is not in the interface language. */
export interface Announcement {
  id: number;
  text: string;
  lang?: string;
}

/** The lines a region keeps: older ones leave it, so it never grows without end. */
const KEPT_ANNOUNCEMENTS = 10;

/**
 * A polite live region's lines, shown by `PoliteRegion`: `announce` adds one (`lang` when it is
 * not in the interface language), `clear` empties the region.
 */
export function useAnnouncer() {
  const [announcements, setAnnouncements] = useState<Announcement[]>([]);

  const announce = (text: string, lang?: string) =>
    setAnnouncements((list) => [
      ...list.slice(1 - KEPT_ANNOUNCEMENTS),
      { id: (list.at(-1)?.id ?? 0) + 1, text, lang },
    ]);

  const clear = () => setAnnouncements([]);

  return { announcements, announce, clear };
}
