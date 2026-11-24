import { CircleAlert } from "lucide-react";
import { Link } from "react-router";
import { inspectionStatuses, permissions } from "../api/contract";
import type { InspectionDetail } from "../api/inspections";
import type { Live } from "../api/live";
import { can, type Establishment, type Me } from "../api/queries";
import { copy, dateTime, format } from "../copy";
import { paths } from "../routes";
import {
  BackLink,
  ChecklistRow,
  ConnectionNotice,
  DetailUnavailable,
  Message,
  RadialProgress,
  RetryButton,
  secondaryButton,
  Skeleton,
  StatusPill,
  Stepper,
} from "../ui/primitives";
import { pillVariant } from "../ui/statuses";
import { ActionPlan } from "./ActionPlan";
import { useInspectionDetail } from "./detail";
import { FindingsCard } from "./FindingsCard";
import {
  checklist,
  DETAIL_TITLE_ID,
  failureText,
  inFlight,
  progressText,
  runningView,
  statusLabel,
} from "./inspections";

const card = "mt-5 rounded-card border border-border bg-card p-5 motion-safe:animate-fade-in";

/**
 * One inspection (`useInspectionDetail`): the header, then the running card (Waiting or Running,
 * with the connection notice inside it), the failed card, or the completed view.
 */
export function InspectionDetailView({
  id,
  establishment,
  me,
  live,
  arrival,
  onBack,
}: {
  id: string;
  establishment: Establishment;
  me: Me | undefined;
  live: Live;
  /** A link from another screen asked for the heading (its location key). */
  arrival?: string;
  /** The back link was chosen (it navigates). */
  onBack: () => void;
}) {
  const { detail, requestedAt, notFound, failed, retrying, retry } = useInspectionDetail({
    id,
    establishmentId: establishment.id,
    arrival,
  });

  const back = (
    <BackLink to={paths.inspections} onClick={onBack}>
      {copy.inspections.back}
    </BackLink>
  );

  if (notFound || failed)
    return (
      <div className="flex flex-col gap-3">
        {back}
        <DetailUnavailable
          titleId={DETAIL_TITLE_ID}
          title={notFound ? copy.inspections.notFoundTitle : copy.inspections.loadErrorTitle}
          body={notFound ? copy.inspections.notFoundBody : copy.inspections.errorBody}
          failed={failed}
          retrying={retrying}
          action={
            notFound ? (
              <Link to={paths.inspections} onClick={onBack} className={secondaryButton}>
                {copy.inspections.backToInspections}
              </Link>
            ) : (
              <RetryButton label={copy.inspections.tryAgain} retrying={retrying} onRetry={retry} />
            )
          }
        />
      </div>
    );

  return (
    <div className="flex flex-col" aria-busy={!detail}>
      <div className="flex flex-col gap-2">
        {back}
        <div className="min-w-0">
          <h2
            id={DETAIL_TITLE_ID}
            tabIndex={-1}
            className="text-greeting font-semibold break-words"
          >
            {requestedAt ? (
              format(copy.inspections.title, { date: dateTime(requestedAt) })
            ) : (
              <>
                <Skeleton className="my-0.5 h-6 w-72 max-w-full" />
                <span className="sr-only">{copy.inspections.loadingInspection}</span>
              </>
            )}
          </h2>
          {detail ? (
            <div className="mt-1 flex flex-wrap items-center gap-2">
              <span className="text-body text-text-secondary">
                {format(copy.inspections.meta, {
                  regulation: detail.regulation,
                  establishment: establishment.name,
                })}
              </span>
              <StatusPill
                variant={pillVariant("inspection", detail.status)}
                label={statusLabel(detail.status)}
              />
            </div>
          ) : (
            <Skeleton className="mt-2 h-3 w-40" />
          )}
        </div>
      </div>

      {!detail ? (
        // The heading says it is loading until the title is known; then the skeleton does.
        <RunningSkeleton label={requestedAt !== undefined} />
      ) : inFlight(detail.status) ? (
        <RunningCard inspection={detail} establishment={establishment} live={live} />
      ) : detail.status === inspectionStatuses.failed ? (
        <div className={card}>
          <Message
            className="items-start"
            icon={<CircleAlert aria-hidden="true" className="size-5 text-text-secondary" />}
            title={copy.inspections.failedTitle}
            body={failureText(detail.failureReasonCode).long}
          />
        </div>
      ) : (
        <>
          <FindingsCard
            inspection={detail}
            canReview={can(me, permissions.inspections.review)}
            canReadDocuments={can(me, permissions.documents.read)}
          />
          <ActionPlan inspection={detail} canReview={can(me, permissions.inspections.review)} />
        </>
      )}
    </div>
  );
}

/**
 * The mock's panel: the connection notice when live updates are paused or stopped (as in the
 * Documents cards), the stepper, the radial progress and the checklist by theme.
 */
function RunningCard({
  inspection,
  establishment,
  live,
}: {
  inspection: InspectionDetail;
  establishment: Establishment;
  live: Live;
}) {
  const view = runningView(inspection, establishment.name);
  const themes = checklist(inspection.themes);
  const { itemsEvaluated: evaluated, itemsTotal: total } = inspection;
  const progress = progressText(evaluated, total);
  return (
    <div className={card}>
      <ConnectionNotice live={live} labels={copy.inspections} className="mb-5" />
      <Stepper
        label={copy.inspections.stepsLabel}
        steps={copy.inspections.steps}
        current={view.step}
        doneLabel={copy.inspections.stepDone}
      />
      <div className="mt-5 flex flex-col gap-5 md:flex-row md:gap-0">
        {/* Until the totals arrive there is no checklist, so the ring has the card's width. */}
        <div
          className={`flex flex-col items-center text-center ${
            themes.length > 0
              ? "md:w-60 md:shrink-0 md:border-r md:border-border md:pr-5"
              : "flex-1"
          }`}
        >
          <RadialProgress
            value={evaluated}
            max={total}
            label={copy.inspections.progressLabel}
            valueText={total > 0 ? progress : view.title}
            title={view.title}
            lines={total > 0 ? [view.body, progress] : [view.body]}
          />
        </div>
        {themes.length > 0 && (
          <ul aria-label={copy.inspections.themesLabel} className="flex-1 md:pl-5">
            {themes.map((theme) => (
              <ChecklistRow
                key={theme.theme}
                progress={theme.progress}
                state={theme.state}
                label={theme.label}
                count={theme.count}
              />
            ))}
          </ul>
        )}
      </div>
      <p className="mt-5 text-small text-text-secondary">{copy.inspections.leaveNote}</p>
    </div>
  );
}

/** A 120px ring and eight checklist bars. */
function RunningSkeleton({ label }: { label: boolean }) {
  return (
    <>
      {label && <span className="sr-only">{copy.inspections.loadingInspection}</span>}
      <div aria-hidden="true" className={card}>
        <div className="flex flex-col gap-5 md:flex-row md:gap-0">
          <div className="flex justify-center md:w-60 md:shrink-0 md:pr-5">
            <span className="block size-30 rounded-pill border-10 border-elevated motion-safe:animate-pulse" />
          </div>
          <ul className="flex flex-1 flex-col md:pl-5">
            {[0, 1, 2, 3, 4, 5, 6, 7].map((row) => (
              <li key={row} className="flex h-9 items-center gap-3">
                <Skeleton className="size-4" />
                <Skeleton className="h-3 w-3/5" />
              </li>
            ))}
          </ul>
        </div>
      </div>
    </>
  );
}
