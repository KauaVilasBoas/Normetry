import { useEffect, useRef } from "react";
import type { Establishment } from "../api/queries";
import { copy } from "../copy";
import { TypeTile } from "../ui/primitives";
import { describeEstablishment } from "./selection";

/**
 * The chat region for the selected establishment. #54 replaces this component; nothing else in
 * the shell depends on what is inside it. A new `focusKey` moves focus to its heading.
 */
export function ChatPlaceholder({
  establishment,
  focusKey,
}: {
  establishment: Establishment;
  focusKey: number;
}) {
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    if (focusKey > 0) heading.current?.focus();
  }, [focusKey]);

  return (
    <section
      aria-labelledby="chat-region-title"
      className="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 rounded-card border border-border bg-card p-5 text-center motion-safe:animate-fade-in"
    >
      <TypeTile type={establishment.type} />
      <h2
        id="chat-region-title"
        ref={heading}
        tabIndex={-1}
        className="mt-1 text-title font-semibold"
      >
        {establishment.name}
      </h2>
      <p className="text-small text-text-secondary">{describeEstablishment(establishment).meta}</p>
      <p className="max-w-90 text-body text-text-secondary">{copy.chat.placeholder}</p>
    </section>
  );
}
