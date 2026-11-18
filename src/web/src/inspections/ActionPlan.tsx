import { CircleAlert } from "lucide-react";
import { useRef, useState } from "react";
import type { ActionItem, InspectionDetail } from "../api/queries";
import { calendarDate, copy, format } from "../copy";
import { useSend } from "../settings/profiles";
import { Checkbox, Message, ReadOnlyCheckbox } from "../ui/primitives";
import { DueBadge } from "./FindingSheet";
import { actionPlan, useReview } from "./findings";

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
  const { send } = useSend();
  const review = useReview(inspection.establishmentId, inspection.id, send, { refetch: false });
  // The status each toggle in flight shows until its answer, by action id.
  const [toggled, setToggled] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const busy = useRef(new Set<string>());
  const rows = actionPlan(inspection.findings);
  const statusOf = (action: ActionItem) => toggled[action.id] ?? action.status;
  const done = rows.filter((row) => statusOf(row.actionItem) === "Done").length;

  const toggle = async (action: ActionItem, checked: boolean) => {
    if (busy.current.has(action.id)) return;
    busy.current.add(action.id);
    setToggled((map) => ({ ...map, [action.id]: checked ? "Done" : "Open" }));
    setErrors((map) => ({ ...map, [action.id]: undefined }));
    const refusal = await review(
      `action-items/${encodeURIComponent(action.id)}/${checked ? "done" : "reopen"}`,
      { method: "POST" },
    );
    busy.current.delete(action.id);
    setToggled((map) => {
      const rest = { ...map };
      delete rest[action.id];
      return rest;
    });
    if (refusal)
      setErrors((map) => ({
        ...map,
        [action.id]: refusal.status === 403 ? copy.inspections.denied : copy.actions.error,
      }));
  };

  return (
    <section
      aria-labelledby={ACTIONS_TITLE_ID}
      className="mt-4 rounded-card border border-border bg-card motion-safe:animate-fade-in"
    >
      <div className="p-5">
        <h2 id={ACTIONS_TITLE_ID} className="text-title font-semibold">
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
            title={copy.actions.emptyTitle}
            body={copy.actions.emptyBody}
          />
        </div>
      ) : (
        <ul className="divide-y divide-border border-t border-border">
          {rows.map(({ actionItem: action, title }) => {
            const isDone = statusOf(action) === "Done";
            const description = format(copy.actions.meta, {
              finding: title,
              date: calendarDate(action.dueOn),
            });
            const error = errors[action.id];
            return (
              <li key={action.id} className="flex flex-wrap items-start gap-x-3 px-5 py-2">
                <div className="w-full min-w-0 sm:w-auto sm:flex-1">
                  {canReview ? (
                    <Checkbox
                      label={action.text}
                      description={description}
                      checked={isDone}
                      onChange={(event) => void toggle(action, event.target.checked)}
                    />
                  ) : (
                    <ReadOnlyCheckbox
                      checked={isDone}
                      state={isDone ? copy.actions.done : copy.actions.open}
                      label={action.text}
                      description={description}
                    />
                  )}
                </div>
                {/* Under the description below 640px, in line with the label's first line from there. */}
                {!isDone && (
                  <div className="pb-2 pl-9 sm:ml-auto sm:pt-2.5 sm:pb-0 sm:pl-0">
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
