import { ArrowDown, ArrowUp, ListFilter, SearchX } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useSearchParams } from "react-router";
import { severities } from "../api/contract";
import type { Finding, InspectionDetail } from "../api/queries";
import { copy, dateTime, format } from "../copy";
import { useSend } from "../settings/profiles";
import { Button, FilterSelect, Message, Sheet, th } from "../ui/primitives";
import { FindingSheet, ReviewPill, VerdictPill } from "./FindingSheet";
import {
  findingsCaption,
  matches,
  severityLabel,
  sortFindings,
  themeLabel,
  themeOptions,
  verdictOptions,
  type Filters,
} from "./findings";
import { FINDINGS_TITLE_ID, findingCount } from "./inspections";

const noFilters: Filters = { verdict: "", theme: "" };
const rowId = (findingId: string) => `finding-${findingId}`;
const focusById = (id: string) => document.getElementById(id)?.focus();

/**
 * The completed view's Findings card: caption, verdict and theme filters, the table sorted by
 * severity, and the finding sheet. Previous and Next walk the table's order as it was when the
 * sheet opened; closing returns focus to the row shown last, or to the heading when that row is
 * filtered out by then. A 403 inside the sheet refetches /me once it closes.
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
  // `?verdict=` and `?theme=` (the Overview's links) preset the filters once, when a code is
  // among the options; then they leave the URL, where filters never stay.
  const [params, setParams] = useSearchParams();
  const [filters, setFilters] = useState<Filters>(() => {
    const known = (options: { value: string }[], value: string | null) =>
      options.some((option) => option.value === value) ? (value ?? "") : "";
    return {
      verdict: known(verdictOptions(findings), params.get("verdict")),
      theme: known(themeOptions(findings), params.get("theme")),
    };
  });
  const filtered = params.has("verdict") || params.has("theme");
  useEffect(() => {
    if (filtered)
      setParams(
        (next) => {
          next.delete("verdict");
          next.delete("theme");
          return next;
        },
        { replace: true },
      );
  }, [filtered, setParams]);
  const [ascending, setAscending] = useState(false);
  const [sheet, setSheet] = useState<{ ids: string[]; index: number }>();
  const [announcements, setAnnouncements] = useState<{ id: number; text: string }[]>([]);
  const lastShown = useRef<string>(undefined);
  const { send, closed } = useSend({ inside: true });
  const rows = sortFindings(findings.filter(matches(filters)), ascending);
  const shown = sheet && findings.find((f) => f.id === sheet.ids[sheet.index]);

  const announce = (text: string) =>
    setAnnouncements((list) => [...list.slice(-9), { id: (list.at(-1)?.id ?? 0) + 1, text }]);

  const open = (index: number) => {
    lastShown.current = rows[index].id;
    setAnnouncements([]);
    setSheet({ ids: rows.map((row) => row.id), index });
  };

  const move = (index: number) => {
    if (!sheet) return;
    const next = findings.find((f) => f.id === sheet.ids[index]);
    if (!next) return;
    lastShown.current = next.id;
    setSheet({ ...sheet, index });
    announce(
      format(copy.findings.moved, {
        title: next.title,
        n: String(index + 1),
        total: String(sheet.ids.length),
      }),
    );
  };

  const close = () => {
    setSheet(undefined);
    closed();
  };

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
              onChange={(event) => setFilters({ ...filters, verdict: event.target.value })}
            />
            <FilterSelect
              label={copy.findings.themeFilter}
              options={themeOptions(findings)}
              value={filters.theme}
              onChange={(event) => setFilters({ ...filters, theme: event.target.value })}
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
          <Button
            variant="secondary"
            className="mt-4"
            onClick={() => {
              // The button goes with the filters: focus moves to the heading first.
              focusById(FINDINGS_TITLE_ID);
              setFilters(noFilters);
            }}
          >
            {copy.findings.clearFilters}
          </Button>
        </div>
      ) : (
        <FindingsTable
          rows={rows}
          caption={format(copy.findings.tableCaption, { date: dateTime(inspection.requestedAt) })}
          ascending={ascending}
          onSort={() => setAscending(!ascending)}
          onOpen={open}
        />
      )}

      <Sheet
        side="right"
        open={shown !== undefined}
        onOpenChange={(next) => {
          if (!next) close();
        }}
        className="w-full sm:w-120"
        // The sheet focuses its title itself; on close, the row shown last or the heading.
        onOpenAutoFocus={(event) => event.preventDefault()}
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          const row = document.getElementById(rowId(lastShown.current ?? ""));
          if (row) row.focus();
          else focusById(FINDINGS_TITLE_ID);
        }}
      >
        {sheet && shown && (
          <FindingSheet
            finding={shown}
            n={sheet.index + 1}
            total={sheet.ids.length}
            onPrevious={sheet.index > 0 ? () => move(sheet.index - 1) : undefined}
            onNext={sheet.index < sheet.ids.length - 1 ? () => move(sheet.index + 1) : undefined}
            onClose={close}
            establishmentId={inspection.establishmentId}
            inspectionId={inspection.id}
            canReview={canReview}
            canReadDocuments={canReadDocuments}
            send={send}
            announce={announce}
          />
        )}
        <div aria-live="polite" className="sr-only">
          {announcements.map((item) => (
            <p key={item.id}>{item.text}</p>
          ))}
        </div>
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
                  className="block w-full text-left text-body font-medium after:absolute after:inset-0 focus-visible:outline-none focus-visible:after:outline-2 focus-visible:after:-outline-offset-2 focus-visible:after:outline-mint"
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
