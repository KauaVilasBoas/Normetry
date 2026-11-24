import { ArrowRight, type LucideIcon } from "lucide-react";
import type { ButtonHTMLAttributes, Ref } from "react";
import { quoteFound, type StoredPassage } from "../api/passages";
import { copy, locale } from "../copy";

/**
 * Opens a citation's source; `aria-expanded` while its passage is the one shown. `icon` leads the
 * label (a document citation's `FileText`); `detail` follows it and keeps up to half the chip, so a
 * long title truncates before the section does.
 */
export function CitationChip({
  label,
  detail,
  expanded,
  icon: Icon,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  label: string;
  detail?: string;
  expanded: boolean;
  icon?: LucideIcon;
}) {
  return (
    <button
      type="button"
      aria-expanded={expanded}
      // Open: the mint border marks it; the fill alone is 1.7:1 against the bubble.
      className={`inline-flex h-7 max-w-full items-center gap-1.5 rounded-sm border px-2.5 text-small font-medium text-mint focus-visible:-outline-offset-2 ${
        expanded ? "border-mint bg-teal-deep" : "border-teal-border hover:bg-inset"
      }`}
      {...props}
    >
      {Icon && <Icon aria-hidden="true" className="size-3.5 shrink-0" />}
      <span className="min-w-0 truncate">{label}</span>
      {detail && <span className="max-w-1/2 shrink-0 truncate">{detail}</span>}
      <ArrowRight aria-hidden="true" className="size-3.5 shrink-0" />
    </button>
  );
}

/**
 * A stored passage in its original language, its quote highlighted (DESIGN.md Quote highlight).
 * `quote` shows the quote alone (the stored `quote` when the offsets don't map); `passage` the
 * whole text, the quote marked when they do. `markRef` gets the highlight, to scroll it into view.
 */
export function QuotedPassage({
  citation,
  mode,
  markRef,
  className = "",
}: {
  citation: Omit<StoredPassage, "sectionPath">;
  mode: "quote" | "passage";
  markRef?: Ref<HTMLElement>;
  className?: string;
}) {
  const { text, language } = citation;
  const found = quoteFound(citation);
  const start = citation.quoteStart ?? 0;
  const end = citation.quoteEnd ?? 0;
  // The markers are read in the interface language, around a quote in its own.
  const quoted = (content: string) => (
    <>
      <span lang={locale} className="sr-only">
        {copy.source.quoteStart}
      </span>
      <mark ref={markRef} className="box-decoration-clone bg-teal-deep text-text-primary">
        {content}
      </mark>
      <span lang={locale} className="sr-only">
        {copy.source.quoteEnd}
      </span>
    </>
  );
  if (mode === "quote")
    return (
      <blockquote
        lang={language}
        className={`rounded-sm bg-inset p-3 text-body whitespace-pre-line ${className}`}
      >
        {quoted(found ? text.slice(start, end) : citation.quote)}
      </blockquote>
    );
  return (
    <div
      lang={language}
      className={`rounded-sm bg-inset p-4 text-body whitespace-pre-line ${className}`}
    >
      {found ? (
        <>
          {text.slice(0, start)}
          {quoted(text.slice(start, end))}
          {text.slice(end)}
        </>
      ) : (
        text
      )}
    </div>
  );
}
