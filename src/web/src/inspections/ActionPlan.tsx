import { CircleAlert, ListChecks } from "lucide-react";
import type { InspectionDetail } from "../api/inspections";
import { calendarDate, copy, format } from "../copy";
import { useRefocusWhenGone } from "../ui/focus";
import { Checkbox, Message, ReadOnlyCheckbox } from "../ui/primitives";
import { DueBadge } from "./FindingSheet";
import { useActionToggles } from "./findings";

const ACTIONS_TITLE_ID = "action-plan-title";

/**
 * The completed view's Action plan: the actions of findings that are not rejected, by due date.
 * With `canReview` each is a checkbox; a toggle shows at once, posts, and reverts with an inline
 * error when it fails. While its request runs, more clicks on it are ignored.
 */
export function ActionPlan({
  inspection,
  canReview,
}: {
  inspection: InspectionDetail;
  canReview: boolean;
}) {
  const { rows, done, isDone, errors, toggle } = useActionToggles(inspection);

  // A 403 takes the checkboxes away (/me is fetched again): focus, lost with the one that was
  // toggled, goes to the heading. The row's error stays.
  useRefocusWhenGone(canReview, ACTIONS_TITLE_ID);

  return (
    <section
      aria-labelledby={ACTIONS_TITLE_ID}
      className="mt-4 rounded-card border border-border bg-card motion-safe:animate-fade-in"
    >
      <div className="p-5">
        <h2 id={ACTIONS_TITLE_ID} tabIndex={-1} className="text-title font-semibold">
          {copy.actions.title}
        </h2>
        {rows.length > 0 && (
          <p className="text-small text-text-secondary tabular-nums">
            {format(copy.actions.caption, {
              open: String(rows.length - done),
              done: String(done),
            })}
          </p>
        )}
      </div>
      {rows.length === 0 ? (
        <div className="px-5 pb-5">
          <Message
            className="items-start"
            icon={<ListChecks aria-hidden="true" className="size-5 text-neutral" />}
            title={copy.actions.emptyTitle}
            body={copy.actions.emptyBody}
          />
        </div>
      ) : (
        <ul className="divide-y divide-border border-t border-border">
          {rows.map(({ actionItem: action, title }) => {
            const checked = isDone(action);
            const description = format(copy.actions.meta, {
              finding: title,
              date: calendarDate(action.dueOn),
            });
            const error = errors[action.id];
            const dueId = `due-${action.id}`;
            return (
              <li key={action.id} className="flex flex-wrap items-start gap-x-3 px-5 py-2">
                <div className="w-full min-w-0 sm:w-auto sm:flex-1">
                  {canReview ? (
                    <Checkbox
                      label={action.text}
                      description={description}
                      // The badge sits outside the label: "Overdue" joins the description.
                      aria-describedby={checked ? undefined : dueId}
                      checked={checked}
                      onChange={(event) => void toggle(action, event.target.checked)}
                    />
                  ) : (
                    <ReadOnlyCheckbox
                      checked={checked}
                      state={checked ? copy.actions.done : copy.actions.open}
                      label={action.text}
                      description={description}
                      dimUnchecked={false}
                    />
                  )}
                </div>
                {/* Under the description below 640px, in line with the label's first line from there. */}
                {!checked && (
                  <div id={dueId} className="pb-2 pl-9 sm:ml-auto sm:pt-2.5 sm:pb-0 sm:pl-0">
                    <DueBadge dueOn={action.dueOn} />
                  </div>
                )}
                {error && (
                  <p role="alert" className="flex w-full items-start gap-1.5 pb-2 pl-9 text-body">
                    <CircleAlert
                      aria-hidden="true"
                      className="size-4 shrink-0 text-text-secondary"
                    />
                    {error}
                  </p>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
