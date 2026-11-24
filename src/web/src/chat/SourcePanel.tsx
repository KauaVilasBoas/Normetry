import { ExternalLink, FileText, X } from "lucide-react";
import { Dialog } from "radix-ui";
import { useEffect, useRef, type ReactNode } from "react";
import { Link } from "react-router";
import { quoteFound, type Citation } from "../api/chat";
import { permissions, sourceTypes } from "../api/contract";
import { can, useMe } from "../api/queries";
import { copy, format, languageName } from "../copy";
import { typeLabel } from "../documents/documents";
import { arrivalState, paths } from "../routes";
import { iconButton, QuotedPassage } from "../ui/primitives";
import { SOURCE_PANEL_ID } from "./Answer";

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
  // Shown the same for both kinds of source: the quote alone when its offsets don't map, then the
  // passage with the quote marked.
  const passage = (
    <>
      {!quoteFound(citation) && (
        <div className="mt-4 rounded-sm bg-inset p-4">
          <p className="text-small font-medium text-text-secondary">{copy.source.quoteLabel}</p>
          <p lang={citation.language} className="mt-1 text-body">
            {citation.quote}
          </p>
        </div>
      )}
      <QuotedPassage citation={citation} mode="passage" markRef={mark} className="mt-4" />
    </>
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
      {citation.sourceType === sourceTypes.document ? (
        <DocumentSource citation={citation}>{passage}</DocumentSource>
      ) : (
        <RegulationSource citation={citation}>{passage}</RegulationSource>
      )}
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

type DocumentCitation = Extract<Citation, { sourceType: typeof sourceTypes.document }>;
type RegulationCitation = Exclude<Citation, DocumentCitation>;

const linkClass =
  "inline-flex items-center gap-1.5 text-body font-medium text-mint hover:underline";

/** The panel's scrolling body (the source, then `children`: the passage) and its footer. */
function SourceBody({
  link,
  note,
  children,
}: {
  link: ReactNode;
  /** Where the passage shown comes from. */
  note: string;
  children: ReactNode;
}) {
  return (
    <>
      <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">{children}</div>
      <footer className="border-t border-border px-5 py-4">
        {link}
        <p className={`text-caption text-text-secondary ${link ? "mt-2" : ""}`}>{note}</p>
      </footer>
    </>
  );
}

/** A regulation's passage: the source, section and version; its official page opens in a new tab. */
function RegulationSource({
  citation,
  children,
}: {
  citation: RegulationCitation;
  children: ReactNode;
}) {
  return (
    <SourceBody
      link={
        citation.officialUrl && (
          <a
            href={citation.officialUrl}
            target="_blank"
            rel="noopener noreferrer"
            className={linkClass}
          >
            <ExternalLink aria-hidden="true" className="size-4 shrink-0" />
            {copy.source.officialPage}
            <span className="sr-only"> {copy.source.newTab}</span>
          </a>
        )
      }
      note={copy.source.storedCopy}
    >
      <p className="text-small text-text-secondary">{citation.sourceTitle}</p>
      <p className="mt-1 text-title font-semibold">{citation.sectionPath}</p>
      <p className="mt-1 text-caption text-text-secondary tabular-nums">
        {format(copy.source.meta, {
          version: citation.sourceVersion,
          language: languageName(citation.language),
        })}
      </p>
      {children}
    </SourceBody>
  );
}

/**
 * One of the establishment's documents: its kind, title, section and version; its page opens in
 * the app, only for those who may read documents.
 */
function DocumentSource({
  citation,
  children,
}: {
  citation: DocumentCitation;
  children: ReactNode;
}) {
  const canReadDocuments = can(useMe().data, permissions.documents.read);
  return (
    <SourceBody
      link={
        canReadDocuments && (
          <Link to={paths.document(citation.documentId)} state={arrivalState} className={linkClass}>
            <FileText aria-hidden="true" className="size-4 shrink-0" />
            {copy.source.openDocument}
          </Link>
        )
      }
      note={copy.source.documentStoredCopy}
    >
      <p className="text-small text-text-secondary">
        {citation.documentType
          ? format(copy.source.documentKindType, {
              type: typeLabel(citation.documentType),
            })
          : copy.source.documentKind}
      </p>
      <p className="mt-1 text-title font-semibold break-words">
        {citation.documentTitle ?? copy.source.untitledDocument}
      </p>
      <p className="mt-1 text-caption text-text-secondary tabular-nums">
        {/* A TXT file, or DOCX text before its first heading, has no section. */}
        {format(citation.sectionPath ? copy.source.documentMeta : copy.source.meta, {
          section: citation.sectionPath,
          version: String(citation.documentVersion),
          language: languageName(citation.language),
        })}
      </p>
      {children}
    </SourceBody>
  );
}
