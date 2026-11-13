import { ExternalLink, X } from "lucide-react";
import { Dialog } from "radix-ui";
import { useEffect, useRef } from "react";
import type { Citation } from "../api/queries";
import { copy, format, locale } from "../copy";
import { iconButton } from "../ui/primitives";
import { SOURCE_PANEL_ID } from "./Answer";

function languageName(code: string): string {
  try {
    return new Intl.DisplayNames([locale], { type: "language" }).of(code) ?? code;
  } catch {
    return code;
  }
}

/**
 * The cited passage as stored with the answer, quote highlighted. Inline from 1280px (`<aside>`),
 * inside a sheet below (then its heading names the dialog). Renders from the citation alone.
 */
export function SourcePanel({
  citation,
  onClose,
  inSheet,
}: {
  citation: Citation;
  onClose: () => void;
  inSheet: boolean;
}) {
  const heading = useRef<HTMLHeadingElement>(null);
  const mark = useRef<HTMLElement>(null);

  // Each passage opened: focus the heading, center the quote (instantly).
  useEffect(() => {
    heading.current?.focus();
    mark.current?.scrollIntoView({ block: "center" });
  }, [citation.id]);

  const { text, quoteStart: start, quoteEnd: end } = citation;
  const found = start !== null && end !== null && start >= 0 && start < end && end <= text.length;

  const title = (
    // In a sheet, Dialog.Title gives it the id that names the dialog; even `id={undefined}` here
    // would override that id.
    <h2
      {...(inSheet ? {} : { id: "source-title" })}
      ref={heading}
      tabIndex={-1}
      className="text-title font-semibold"
    >
      {copy.source.title}
    </h2>
  );
  const content = (
    <>
      <header className="flex items-center justify-between gap-2 border-b border-border py-3 pr-3 pl-5">
        {inSheet ? <Dialog.Title asChild>{title}</Dialog.Title> : title}
        <button
          type="button"
          aria-label={copy.source.close}
          onClick={onClose}
          className={iconButton}
        >
          <X aria-hidden="true" className="size-5" />
        </button>
      </header>
      <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">
        <p className="text-small text-text-secondary">{citation.sourceTitle}</p>
        <p className="mt-1 text-title font-semibold">{citation.sectionPath}</p>
        <p className="mt-1 text-caption text-text-secondary tabular-nums">
          {format(copy.source.meta, {
            version: citation.sourceVersion,
            language: languageName(citation.language),
          })}
        </p>
        {!found && (
          <div className="mt-4 rounded-sm bg-inset p-4">
            <p className="text-small font-medium text-text-secondary">{copy.source.quoteLabel}</p>
            <p lang={citation.language} className="mt-1 text-body">
              {citation.quote}
            </p>
          </div>
        )}
        <div
          lang={citation.language}
          className="mt-4 rounded-sm bg-inset p-4 text-body whitespace-pre-line"
        >
          {found ? (
            <>
              {text.slice(0, start)}
              <span lang={locale} className="sr-only">
                {copy.source.quoteStart}
              </span>
              <mark ref={mark} className="box-decoration-clone bg-teal-deep text-text-primary">
                {text.slice(start, end)}
              </mark>
              <span lang={locale} className="sr-only">
                {copy.source.quoteEnd}
              </span>
              {text.slice(end)}
            </>
          ) : (
            text
          )}
        </div>
      </div>
      <footer className="border-t border-border px-5 py-4">
        {citation.officialUrl && (
          <a
            href={citation.officialUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-1.5 text-body font-medium text-mint hover:underline"
          >
            <ExternalLink aria-hidden="true" className="size-4 shrink-0" />
            {copy.source.officialPage}
            <span className="sr-only"> {copy.source.newTab}</span>
          </a>
        )}
        <p className={`text-caption text-text-secondary ${citation.officialUrl ? "mt-2" : ""}`}>
          {copy.source.storedCopy}
        </p>
      </footer>
    </>
  );

  return inSheet ? (
    <div id={SOURCE_PANEL_ID} className="flex min-h-0 flex-1 flex-col">
      {content}
    </div>
  ) : (
    <aside
      id={SOURCE_PANEL_ID}
      aria-labelledby="source-title"
      onKeyDown={(event) => {
        if (event.key === "Escape") onClose();
      }}
      className="flex w-90 shrink-0 flex-col rounded-card border border-border bg-card motion-safe:animate-fade-in"
    >
      {content}
    </aside>
  );
}
