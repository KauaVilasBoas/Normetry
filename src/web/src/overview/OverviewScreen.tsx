import {
  ArrowDown,
  ArrowRight,
  ArrowUp,
  CalendarClock,
  ChartLine,
  ClipboardCheck,
  ClipboardList,
  FileText,
  ListTodo,
  SearchX,
  ShieldCheck,
} from "lucide-react";
import { useId, type ReactNode } from "react";
import { Link } from "react-router";
import { permissions, verdicts } from "../api/contract";
import {
  useOverview,
  type CompliancePoint,
  type LatestInspection,
  type Overview,
} from "../api/overview";
import { can, useQueryRetry, type Establishment, type Me } from "../api/queries";
import { calendarDate, copy, format, percent, shortDay } from "../copy";
import { typeLabel } from "../documents/documents";
import { ExpiryBadge } from "../documents/parts";
import { verdictLabel } from "../inspections/findings";
import {
  findingCount,
  regulationOf,
  themeLabel,
  verdictCountsText,
} from "../inspections/inspections";
import { arrivalState, paths } from "../routes";
import { AreaChart, DonutChart } from "../ui/charts";
import { useShellFocusRequest } from "../ui/focus";
import {
  Disclosure,
  ErrorMessage,
  KpiCard,
  Message,
  ProgressBar,
  rowLink,
  secondaryButton,
  Skeleton,
  StatusPill,
  th,
} from "../ui/primitives";
import {
  delta,
  plotted,
  pointsText,
  rowThreeSpans,
  share,
  themeShare,
  TITLE_ID,
  verdictTotal,
  type RowThreeCard,
} from "./overview";

const card =
  "overflow-hidden rounded-card border border-border bg-card motion-safe:animate-fade-in";
const cardHeader = "px-5 pt-5";
const legendRow = "-mx-2 flex min-h-9 items-center gap-2 rounded-sm px-2";
const emptyIcon = "size-5 text-neutral";
const noResult = (
  <>
    <span aria-hidden="true">—</span>
    <span className="sr-only">{copy.overview.noResult}</span>
  </>
);

/** In donut and legend order (spec §8.4: data is blue, red only for the non-compliant share). */
const verdictRows = [
  { code: verdicts.compliant, key: "compliant", dot: "bg-chart-2", stroke: "stroke-chart-2" },
  {
    code: verdicts.insufficientEvidence,
    key: "insufficientEvidence",
    dot: "bg-chart-1",
    stroke: "stroke-chart-1",
  },
  { code: verdicts.nonCompliant, key: "nonCompliant", dot: "bg-danger", stroke: "stroke-danger" },
] as const;

/**
 * Overview (spec overview-screen.md): the selected establishment's latest inspection results and
 * its documents to renew. Sections follow the response, which leaves out what the caller cannot
 * read; `/me` decides only the skeleton. A new `focusKey` (the drawer chose an establishment, or a
 * retry loaded the list) moves focus to the heading.
 */
export function OverviewScreen({
  establishment,
  me,
  focusKey,
}: {
  establishment: Establishment;
  me: Me | undefined;
  focusKey: number;
}) {
  const inspectionsRead = can(me, permissions.inspections.read);
  const documentsRead = can(me, permissions.documents.read);
  const overview = useOverview(establishment.id, inspectionsRead, documentsRead);
  const { failed, retrying, retry } = useQueryRetry(overview, TITLE_ID);
  const data = failed ? undefined : overview.data;
  const latest = data?.latestInspection ?? undefined;
  const regulation = regulationOf(establishment);

  useShellFocusRequest(focusKey, TITLE_ID);

  return (
    <div aria-busy={(!failed && !data) || retrying} className="flex flex-col">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 id={TITLE_ID} tabIndex={-1} className="text-greeting font-semibold break-words">
            {establishment.name}
          </h2>
          {!data && !failed ? (
            <Skeleton className="mt-1.5 h-3 w-64 max-w-full" />
          ) : (
            <p className="text-body text-text-secondary tabular-nums">
              {latest
                ? format(copy.overview.results, {
                    regulation,
                    date: shortDay(latest.completedAt),
                  })
                : regulation}
            </p>
          )}
        </div>
        {latest && (
          <Link
            to={paths.inspection(latest.id)}
            state={arrivalState}
            className={`${secondaryButton} w-full sm:w-auto`}
          >
            <ClipboardCheck aria-hidden="true" className="size-4" />
            {copy.overview.viewInspection}
          </Link>
        )}
      </div>

      {failed ? (
        <div className={`mt-5 px-5 pt-1 pb-5 ${card}`}>
          <ErrorMessage
            title={copy.overview.errorTitle}
            body={copy.overview.errorBody}
            retryLabel={copy.overview.tryAgain}
            retrying={retrying}
            onRetry={retry}
          />
        </div>
      ) : data ? (
        <Sections
          data={data}
          establishment={establishment}
          canRun={can(me, permissions.inspections.run)}
        />
      ) : (
        <OverviewSkeleton inspections={inspectionsRead} documents={documentsRead} />
      )}
    </div>
  );
}

/** The rows by state (spec §4): KPIs, then the results or a message, then the lists. */
function Sections({
  data,
  establishment,
  canRun,
}: {
  data: Overview;
  establishment: Establishment;
  canRun: boolean;
}) {
  const inspections = "latestInspection" in data;
  const latest = data.latestInspection ?? undefined;
  const { documents } = data;
  const series = data.series ?? [];
  const results = latest !== undefined && latest.compliance !== null;
  const row3: RowThreeCard[] = [];
  if (latest) row3.push("trend");
  if (documents) row3.push("documents");
  if (results) row3.push("risk");
  const span = rowThreeSpans(row3);

  return (
    <div className="mt-5 flex flex-col gap-4">
      {(latest || documents) && (
        <dl
          className={`grid gap-4 sm:grid-cols-2 ${latest && documents ? "xl:grid-cols-4" : "xl:grid-cols-3"}`}
        >
          {latest && (
            <>
              <KpiCard
                label={copy.overview.compliance}
                Icon={ShieldCheck}
                value={
                  latest.compliance === null
                    ? noResult
                    : pointsText(
                        share(latest.verdictCounts.compliant, verdictTotal(latest.verdictCounts)),
                      )
                }
              >
                <Delta series={series} />
              </KpiCard>
              <KpiCard
                label={copy.overview.openActions}
                Icon={ListTodo}
                value={data.openActionItems ?? 0}
              >
                {(data.overdueActionItems ?? 0) > 0 ? (
                  <StatusPill
                    variant="attention"
                    label={format(copy.overview.overdue, {
                      count: String(data.overdueActionItems),
                    })}
                  />
                ) : (
                  copy.overview.noneOverdue
                )}
              </KpiCard>
              <KpiCard
                label={copy.overview.toReview}
                Icon={ClipboardList}
                value={data.pendingReviews ?? 0}
              >
                {(data.pendingReviews ?? 0) > 0
                  ? copy.overview.waitingReview
                  : copy.overview.allReviewed}
              </KpiCard>
            </>
          )}
          {documents && (
            <KpiCard
              label={copy.overview.toRenew}
              Icon={CalendarClock}
              value={documents.expired + documents.expiringSoon}
            >
              {documents.expired > 0 && (
                <StatusPill
                  variant="attention"
                  label={format(copy.overview.expired, { count: String(documents.expired) })}
                />
              )}
              {documents.expiringSoon > 0 && (
                <span>
                  {format(copy.overview.expiring, { count: String(documents.expiringSoon) })}
                </span>
              )}
              {documents.expired + documents.expiringSoon === 0 && copy.overview.nothingToRenew}
            </KpiCard>
          )}
        </dl>
      )}

      {inspections && !latest ? (
        <div className={`p-5 ${card}`}>
          <Message
            className="items-start"
            icon={<ClipboardCheck aria-hidden="true" className={emptyIcon} />}
            title={copy.overview.emptyTitle}
            body={format(canRun ? copy.overview.emptyBody : copy.overview.emptyBodyReadOnly, {
              establishment: establishment.name,
              regulation: regulationOf(establishment),
            })}
          />
          <Link to={paths.inspections} state={arrivalState} className={`${secondaryButton} mt-4`}>
            {copy.overview.goToInspections}
          </Link>
        </div>
      ) : latest && !results ? (
        <div className={`p-5 ${card}`}>
          <Message
            className="items-start"
            icon={<SearchX aria-hidden="true" className={emptyIcon} />}
            title={copy.overview.noResultsTitle}
            body={format(copy.overview.noResultsBody, { date: shortDay(latest.completedAt) })}
          />
        </div>
      ) : (
        latest && (
          <div className="grid gap-4 xl:grid-cols-3">
            <VerdictsCard latest={latest} />
            <ThemesCard latest={latest} />
          </div>
        )
      )}

      {row3.length > 0 && (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-12">
          {latest && <TrendCard series={series} className={span.trend} />}
          {documents && (
            <DocumentsCard
              documents={documents}
              establishment={establishment}
              className={span.documents}
            />
          )}
          {latest && results && <RiskCard latest={latest} className={span.risk} />}
        </div>
      )}
    </div>
  );
}

/** The Compliance KPI's footer: the change from the previous inspection with a result. */
function Delta({ series }: { series: CompliancePoint[] }) {
  const change = delta(series);
  if (change === undefined) return null;
  if (change === null) return copy.overview.noPrevious;
  if (change === 0) return copy.overview.deltaSame;
  const up = change > 0;
  const points = String(Math.abs(change));
  const Arrow = up ? ArrowUp : ArrowDown;
  return (
    <>
      <span
        aria-hidden="true"
        className={`inline-flex items-center gap-1 font-medium ${up ? "text-success" : "text-danger"}`}
      >
        <Arrow className="size-3.5" />
        {format(copy.overview.deltaPoints, { points })}
      </span>
      <span aria-hidden="true">{copy.overview.deltaVs}</span>
      <span className="sr-only">
        {format(up ? copy.overview.deltaUp : copy.overview.deltaDown, { points })}
      </span>
    </>
  );
}

function CardHeader({
  id,
  title,
  caption,
  action,
}: {
  id: string;
  title: string;
  caption?: string;
  action?: ReactNode;
}) {
  return (
    <div className={`flex flex-wrap items-start justify-between gap-3 ${cardHeader}`}>
      <div className="min-w-0">
        <h3 id={id} className="text-title font-semibold">
          {title}
        </h3>
        {caption && <p className="text-small text-text-secondary tabular-nums">{caption}</p>}
      </div>
      {action}
    </div>
  );
}

/** The donut and its legend, whose rows open the findings with that verdict. */
function VerdictsCard({ latest }: { latest: LatestInspection }) {
  const counts = latest.verdictCounts;
  const total = verdictTotal(counts);
  return (
    <section aria-labelledby="verdicts-title" className={`@container p-5 ${card}`}>
      <h3 id="verdicts-title" className="text-title font-semibold">
        {copy.overview.verdicts}
      </h3>
      <p className="text-small text-text-secondary">{copy.overview.verdictsCaption}</p>
      <div className="mt-4 flex flex-col items-center gap-5 @sm:flex-row">
        <DonutChart
          segments={verdictRows.map((row) => ({ value: counts[row.key], className: row.stroke }))}
          value={pointsText(share(counts.compliant, total))}
          caption={verdictLabel(verdicts.compliant)}
        />
        <ul aria-labelledby="verdicts-title" className="w-full @sm:flex-1">
          {verdictRows.map((row) => {
            const count = counts[row.key];
            const content = (
              <>
                <span aria-hidden="true" className={`size-2 shrink-0 rounded-pill ${row.dot}`} />
                <span className="flex-1 text-body text-text-secondary">
                  {verdictLabel(row.code)}
                </span>
                <span className="text-body font-medium tabular-nums">
                  {pointsText(share(count, total))}
                </span>
                <span aria-hidden="true" className="text-small text-text-secondary tabular-nums">
                  ({count})
                </span>
                <span className="sr-only">{findingCount(count)}</span>
              </>
            );
            // A verdict with no findings has nothing to open.
            return (
              <li key={row.code}>
                {count > 0 ? (
                  <Link
                    to={paths.inspection(latest.id, { verdict: row.code })}
                    state={arrivalState}
                    className={`${legendRow} hover:bg-elevated`}
                  >
                    {content}
                  </Link>
                ) : (
                  <div className={legendRow}>{content}</div>
                )}
              </li>
            );
          })}
        </ul>
      </div>
    </section>
  );
}

const countHeader = `${th.replace("text-left", "text-right")} hidden md:table-cell`;
const countCell = "hidden px-3 py-2.5 text-right text-body tabular-nums md:table-cell";

/** The latest inspection's verdicts by theme, in checklist order; a row opens its findings. */
function ThemesCard({ latest }: { latest: LatestInspection }) {
  const id = useId();
  return (
    <section aria-labelledby="themes-title" className={`xl:col-span-2 ${card}`}>
      <CardHeader id="themes-title" title={copy.overview.byTheme} />
      <table className="mt-5 w-full">
        <caption className="sr-only">
          {format(copy.overview.byThemeTable, { date: shortDay(latest.completedAt) })}
        </caption>
        <thead className="bg-inset">
          <tr>
            <th scope="col" className={`${th} pl-5`}>
              {copy.findings.theme}
            </th>
            {verdictRows.map((row) => (
              <th key={row.code} scope="col" className={countHeader}>
                {verdictLabel(row.code)}
              </th>
            ))}
            <th scope="col" className={`${th} pr-5`}>
              {copy.overview.compliance}
            </th>
          </tr>
        </thead>
        <tbody>
          {latest.byTheme.map((row) => {
            const complianceId = `${id}-${row.theme}`;
            const value = themeShare(row);
            return (
              <tr
                key={row.theme}
                className="relative h-14 border-t border-border hover:bg-elevated"
              >
                <td className="w-full max-w-0 py-2.5 pr-3 pl-5">
                  <Link
                    to={paths.inspection(latest.id, { theme: row.theme })}
                    state={arrivalState}
                    aria-describedby={complianceId}
                    className={`${rowLink} truncate`}
                  >
                    {themeLabel(row.theme)}
                  </Link>
                  {/* Below 768px the count columns are hidden; the counts show here. */}
                  <span className="block text-small text-text-secondary tabular-nums md:hidden">
                    {verdictCountsText(row)}
                  </span>
                </td>
                {verdictRows.map((verdict) => (
                  <td key={verdict.code} className={countCell}>
                    {row[verdict.key]}
                  </td>
                ))}
                <td id={complianceId} className="py-2.5 pr-5 pl-3">
                  <span className="flex items-center gap-2">
                    <ProgressBar ratio={value / 100} />
                    <span className="w-10 text-right text-small font-medium text-text-primary tabular-nums">
                      {pointsText(value)}
                    </span>
                  </span>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </section>
  );
}

/** Compliance across the last completed inspections: the chart, then its values as a table. */
function TrendCard({ series, className }: { series: CompliancePoint[]; className: string }) {
  const points = plotted(series);
  const first = points[0];
  const last = points.at(-1);
  const count = String(series.length);
  return (
    <section aria-labelledby="trend-title" className={`${card} ${className}`}>
      <CardHeader
        id="trend-title"
        title={copy.overview.trend}
        caption={points.length >= 2 ? format(copy.overview.trendCaption, { count }) : undefined}
      />
      {first && last && points.length >= 2 ? (
        <div className="p-5">
          <AreaChart
            points={points}
            summary={format(copy.overview.trendSummary, {
              count,
              first: percent(first.ratio),
              firstDate: first.label,
              last: percent(last.ratio),
              lastDate: last.label,
            })}
          />
          <Disclosure summary={copy.overview.showData} className="mt-3">
            <table className="mt-2 w-full">
              <caption className="sr-only">{copy.overview.trendTable}</caption>
              <thead className="bg-inset">
                <tr>
                  <th scope="col" className={th}>
                    {copy.overview.inspection}
                  </th>
                  <th scope="col" className={th}>
                    {copy.overview.compliance}
                  </th>
                </tr>
              </thead>
              <tbody>
                {series.toReversed().map((point) => (
                  <tr key={point.inspectionId} className="border-t border-border">
                    <td className="px-3 py-2.5">
                      <Link
                        to={paths.inspection(point.inspectionId)}
                        state={arrivalState}
                        className="rounded-sm text-body font-medium tabular-nums hover:underline"
                      >
                        {shortDay(point.completedAt)}
                      </Link>
                    </td>
                    <td className="px-3 py-2.5 text-body tabular-nums">
                      {point.compliance === null ? noResult : percent(point.compliance)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Disclosure>
        </div>
      ) : (
        <div className="px-5 pb-5">
          <Message
            icon={<ChartLine aria-hidden="true" className={emptyIcon} />}
            title={copy.overview.noTrendTitle}
            body={copy.overview.noTrendBody}
          />
        </div>
      )}
    </section>
  );
}

/** Expired documents and those expiring within 30 days, soonest first; a row opens one. */
function DocumentsCard({
  documents,
  establishment,
  className,
}: {
  documents: NonNullable<Overview["documents"]>;
  establishment: Establishment;
  className: string;
}) {
  const id = useId();
  const { list } = documents;
  return (
    <section aria-labelledby="expiring-title" className={`${card} ${className}`}>
      <CardHeader
        id="expiring-title"
        title={copy.overview.documents}
        caption={copy.overview.documentsCaption}
        action={
          // The API lists the soonest ten.
          documents.expired + documents.expiringSoon > list.length && (
            <Link to={paths.documents} state={arrivalState} className={secondaryButton}>
              {copy.overview.viewAllDocuments}
              <ArrowRight aria-hidden="true" className="size-4" />
            </Link>
          )
        }
      />
      {list.length === 0 ? (
        <div className="px-5 pb-5">
          <Message
            icon={<FileText aria-hidden="true" className={emptyIcon} />}
            title={copy.overview.noDocumentsTitle}
            body={format(copy.overview.noDocumentsBody, { establishment: establishment.name })}
          />
        </div>
      ) : (
        <ul className="mt-5 divide-y divide-border border-t border-border">
          {list.map((document) => {
            const badgeId = `${id}-${document.id}`;
            return (
              <li key={document.id} className="relative min-h-14 px-5 py-2.5 hover:bg-elevated">
                <Link
                  to={paths.document(document.id)}
                  state={arrivalState}
                  aria-describedby={badgeId}
                  className={`${rowLink} truncate`}
                >
                  {document.title}
                </Link>
                {/* Under the title, not beside it, so it fits a 3/12 column. */}
                <span className="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-1">
                  <span className="text-small text-text-secondary tabular-nums">
                    {format(copy.overview.documentMeta, {
                      type: typeLabel(document.type),
                      date: calendarDate(document.expiresOn),
                    })}
                  </span>
                  <span id={badgeId} className="flex">
                    <ExpiryBadge expiresOn={document.expiresOn} />
                  </span>
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

/** The (at most 3) themes most at risk, in the API's order; a row opens its findings. */
function RiskCard({ latest, className }: { latest: LatestInspection; className: string }) {
  const id = useId();
  const risks = latest.topRiskThemes;
  return (
    <section aria-labelledby="risk-title" className={`${card} ${className}`}>
      <CardHeader id="risk-title" title={copy.overview.risk} caption={copy.overview.riskCaption} />
      {risks.length === 0 ? (
        <div className="px-5 pb-5">
          <Message
            icon={<ShieldCheck aria-hidden="true" className={emptyIcon} />}
            title={copy.overview.noRiskTitle}
            body={copy.overview.noRiskBody}
          />
        </div>
      ) : (
        <ol className="mt-5 divide-y divide-border border-t border-border">
          {risks.map((risk, index) => {
            const counts = latest.byTheme.find((row) => row.theme === risk.theme);
            const countsId = `${id}-${risk.theme}`;
            return (
              <li
                key={risk.theme}
                className="relative flex min-h-14 items-center gap-3 px-5 py-2.5 hover:bg-elevated"
              >
                {/* The list numbers its items for screen readers. */}
                <span
                  aria-hidden="true"
                  className="flex size-6 shrink-0 items-center justify-center rounded-pill bg-inset text-small font-semibold tabular-nums"
                >
                  {index + 1}
                </span>
                <span className="min-w-0 flex-1">
                  <Link
                    to={paths.inspection(latest.id, { theme: risk.theme })}
                    state={arrivalState}
                    aria-describedby={countsId}
                    className={`${rowLink} truncate`}
                  >
                    {themeLabel(risk.theme)}
                  </Link>
                  <span id={countsId} className="block text-small text-text-secondary tabular-nums">
                    {format(copy.overview.riskCounts, {
                      nonCompliant: String(counts?.nonCompliant ?? 0),
                      insufficient: String(counts?.insufficientEvidence ?? 0),
                    })}
                  </span>
                </span>
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}

/** Skeleton rows (`count` bars) under a card's title bar. */
function SkeletonCard({ rows, className = "" }: { rows: number; className?: string }) {
  return (
    <div className={`${card} ${className}`}>
      <div className={cardHeader}>
        <Skeleton className="h-4 w-32" />
      </div>
      <ul className="mt-5 border-t border-border">
        {Array.from({ length: rows }, (_, row) => (
          <li
            key={row}
            className="flex h-14 items-center border-t border-border px-5 first:border-t-0"
          >
            <Skeleton className="h-3 w-3/5" />
          </li>
        ))}
      </ul>
    </div>
  );
}

/** What `/me` lets the response hold, as skeletons. */
function OverviewSkeleton({
  inspections,
  documents,
}: {
  inspections: boolean;
  documents: boolean;
}) {
  const kpis = (inspections ? 3 : 0) + (documents ? 1 : 0);
  const row3: RowThreeCard[] = [];
  if (inspections) row3.push("trend");
  if (documents) row3.push("documents");
  if (inspections) row3.push("risk");
  const span = rowThreeSpans(row3);
  return (
    <>
      <span className="sr-only">{copy.overview.loading}</span>
      <div aria-hidden="true" className="mt-5 flex flex-col gap-4">
        <div
          className={`grid gap-4 sm:grid-cols-2 ${kpis === 4 ? "xl:grid-cols-4" : "xl:grid-cols-3"}`}
        >
          {Array.from({ length: kpis }, (_, kpi) => (
            <div key={kpi} className={`p-4 ${card}`}>
              <Skeleton className="h-3 w-24" />
              <Skeleton className="mt-3 h-7 w-16" />
              <Skeleton className="mt-3 h-3 w-32" />
            </div>
          ))}
        </div>
        {inspections && (
          <div className="grid gap-4 xl:grid-cols-3">
            <div className={`p-5 ${card}`}>
              <Skeleton className="h-4 w-24" />
              <span className="mx-auto mt-4 block size-35 rounded-pill border-14 border-elevated motion-safe:animate-pulse" />
            </div>
            <SkeletonCard rows={5} className="xl:col-span-2" />
          </div>
        )}
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-12">
          {inspections && (
            <div className={`p-5 ${card} ${span.trend}`}>
              <Skeleton className="h-4 w-40" />
              <Skeleton className="mt-5 h-40 w-full" />
            </div>
          )}
          {documents && <SkeletonCard rows={3} className={span.documents} />}
          {inspections && <SkeletonCard rows={3} className={span.risk} />}
        </div>
      </div>
    </>
  );
}
