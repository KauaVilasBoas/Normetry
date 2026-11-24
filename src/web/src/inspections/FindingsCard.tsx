import { ArrowDown, ArrowUp, ListFilter, SearchX } from "lucide-react";
import { useState } from "react";
import { severities } from "../api/contract";
import { findingPaths, type Finding, type InspectionDetail } from "../api/inspections";
import { copy, dateTime, format } from "../copy";
import { Button, FilterSelect, Message, PoliteRegion, rowLink, Sheet, th } from "../ui/primitives";
import { FindingSheet, ReviewPill, VerdictPill } from "./FindingSheet";
import {
  findingsCaption,
  matches,
  rowId,
  severityLabel,
  sortFindings,
  themeLabel,
  themeOptions,
  useFindingSheet,
  usePresetFilters,
  useReview,
  verdictOptions,
} from "./findings";
import { FINDINGS_TITLE_ID, findingCount } from "./inspections";

/**
 * The completed view's Findings card: caption, verdict and theme filters (`usePresetFilters`), the
 * table sorted by severity, and the finding sheet (`useFindingSheet`), whose reviews it sends.
 */
export function FindingsCard({
  inspection,
  canReview,
  canReadDocuments,
}: {
  inspection: InspectionDetail;
  canReview: boolean;
  canReadDocuments: boolean;
}) {
  const { findings } = inspection;
  const { filters, filterBy, clearFilters } = usePresetFilters(findings);
  const [ascending, setAscending] = useState(false);
  const rows = sortFindings(findings.filter(matches(filters)), ascending);
  const sheet = useFindingSheet(findings, rows);
  const review = useReview(inspection.establishmentId, inspection.id, sheet.send);

  return (
    <section
      aria-labelledby={FINDINGS_TITLE_ID}
      className="mt-5 rounded-card border border-border bg-card motion-safe:animate-fade-in"
    >
      <div className="flex flex-wrap items-start justify-between gap-3 p-5">
        <div className="min-w-0">
          <h2 id={FINDINGS_TITLE_ID} tabIndex={-1} className="text-title font-semibold">
            {copy.findings.title}
          </h2>
          <p className="text-small text-text-secondary tabular-nums">
            {findings.length > 0 ? findingsCaption(findings) : findingCount(0)}
          </p>
        </div>
        {findings.length > 0 && (
          <div className="flex w-full flex-wrap gap-2 sm:w-auto">
            <FilterSelect
              label={copy.findings.verdictFilter}
              options={verdictOptions(findings)}
              value={filters.verdict}
              onChange={(event) => filterBy("verdict", event.target.value)}
            />
            <FilterSelect
              label={copy.findings.themeFilter}
              options={themeOptions(findings)}
              value={filters.theme}
              onChange={(event) => filterBy("theme", event.target.value)}
            />
          </div>
        )}
      </div>

      {findings.length === 0 ? (
        <div className="px-5 pb-5">
          <Message
            className="items-start"
            icon={<SearchX aria-hidden="true" className="size-5 text-neutral" />}
            title={copy.findings.noneTitle}
            body={copy.findings.noneBody}
          />
        </div>
      ) : rows.length === 0 ? (
        <div className="px-5 pb-5">
          <Message
            className="items-start"
            icon={<ListFilter aria-hidden="true" className="size-5 text-neutral" />}
            title={copy.findings.noMatchTitle}
            body={copy.findings.noMatchBody}
          />
          <Button variant="secondary" className="mt-4" onClick={clearFilters}>
            {copy.findings.clearFilters}
          </Button>
        </div>
      ) : (
        <FindingsTable
          rows={rows}
          caption={format(copy.findings.tableCaption, { date: dateTime(inspection.requestedAt) })}
          ascending={ascending}
          onSort={() => setAscending(!ascending)}
          onOpen={sheet.open}
        />
      )}

      <Sheet
        side="right"
        open={sheet.shown !== undefined}
        onOpenChange={sheet.openChange}
        className="w-full sm:w-120"
        // The sheet focuses its title itself; on close, the row shown last or the heading.
        onOpenAutoFocus={(event) => event.preventDefault()}
        onCloseAutoFocus={sheet.afterClose}
      >
        {sheet.shown && (
          <FindingSheet
            finding={sheet.shown}
            n={sheet.n}
            total={sheet.total}
            onPrevious={sheet.previous}
            onNext={sheet.next}
            onClose={sheet.close}
            canReview={canReview}
            canReadDocuments={canReadDocuments}
            review={review}
            reviewPaths={findingPaths(inspection.establishmentId, inspection.id, sheet.shown.id)}
            announce={sheet.announce}
          />
        )}
        <PoliteRegion announcements={sheet.announcements} />
      </Sheet>
    </section>
  );
}

/**
 * Requirement · Theme (from 1280px) · Severity (sortable) · Verdict · Review (from 768px). Below
 * 768px the requirement cell carries the rest; each row button is described by the Verdict and
 * Review cells at every width, so the visual pills there are hidden from screen readers.
 */
function FindingsTable({
  rows,
  caption,
  ascending,
  onSort,
  onOpen,
}: {
  rows: Finding[];
  caption: string;
  ascending: boolean;
  onSort: () => void;
  onOpen: (index: number) => void;
}) {
  const SortIcon = ascending ? ArrowUp : ArrowDown;
  return (
    <table className="w-full">
      <caption className="sr-only">{caption}</caption>
      <thead className="bg-inset">
        <tr>
          {/* The last column below 768px. */}
          <th scope="col" className={`${th} pr-5 pl-5 md:pr-3`}>
            {copy.findings.requirement}
          </th>
          <th scope="col" className={`${th} hidden xl:table-cell`}>
            {copy.findings.theme}
          </th>
          <th
            scope="col"
            aria-sort={ascending ? "ascending" : "descending"}
            className={`${th} hidden md:table-cell`}
          >
            <button
              type="button"
              onClick={onSort}
              className="inline-flex items-center gap-1 rounded-sm hover:text-text-primary"
            >
              {copy.findings.severity}
              <SortIcon aria-hidden="true" className="size-3.5" />
            </button>
          </th>
          <th scope="col" className={`${th} hidden md:table-cell`}>
            {copy.findings.verdict}
          </th>
          <th scope="col" className={`${th} hidden pr-5 md:table-cell`}>
            {copy.findings.review}
          </th>
        </tr>
      </thead>
      <tbody>
        {rows.map((finding, index) => {
          const id = rowId(finding.id);
          const theme = themeLabel(finding.theme);
          const severity = severityLabel(finding.severity);
          return (
            <tr key={finding.id} className="relative h-14 border-t border-border hover:bg-elevated">
              <td className="w-full max-w-0 py-2.5 pr-5 pl-5 md:pr-3">
                {/* Stretched over the row: the whole row opens it, with one tab stop. */}
                <button
                  id={id}
                  type="button"
                  aria-haspopup="dialog"
                  aria-describedby={`${id}-verdict ${id}-review`}
                  onClick={() => onOpen(index)}
                  className={`${rowLink} w-full text-left`}
                >
                  <span className="line-clamp-2">{finding.title}</span>
                </button>
                <span className="block truncate text-small text-text-secondary md:hidden">
                  {format(copy.findings.meta, { severity, theme })}
                </span>
                <span className="hidden truncate text-small text-text-secondary md:block xl:hidden">
                  {theme}
                </span>
                <span aria-hidden="true" className="mt-1.5 flex flex-wrap gap-1.5 md:hidden">
                  <VerdictPill verdict={finding.verdict} />
                  <ReviewPill status={finding.reviewStatus} />
                </span>
              </td>
              <td className="hidden px-3 py-2.5 text-body text-text-secondary xl:table-cell">
                {theme}
              </td>
              <td
                className={`hidden px-3 py-2.5 text-body whitespace-nowrap md:table-cell ${
                  finding.severity === severities.critical
                    ? "font-semibold text-text-primary"
                    : "text-text-secondary"
                }`}
              >
                {severity}
              </td>
              <td id={`${id}-verdict`} className="hidden px-3 py-2.5 md:table-cell">
                <VerdictPill verdict={finding.verdict} />
              </td>
              <td id={`${id}-review`} className="hidden py-2.5 pr-5 pl-3 md:table-cell">
                <ReviewPill status={finding.reviewStatus} />
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
