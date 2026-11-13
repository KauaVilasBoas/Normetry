import { useQueryClient } from "@tanstack/react-query";
import { Clock, SendHorizontal } from "lucide-react";
import { useEffect, useLayoutEffect, useState, type RefObject } from "react";
import { quotaKey, type ChatQuota } from "../api/queries";
import { copy, format } from "../copy";
import { quotaNoticeText } from "./answers";

/** The AI route's limit on a question (chat API, #51). */
const MAX_QUESTION_LENGTH = 2000;
const MAX_LINES = 6;
export const COMPOSER_ID = "chat-question";
const NOTICE_ID = "quota-notice";

/** The textarea, the send button and the daily quota (counter, or the notice at zero). */
export function Composer({
  value,
  onChange,
  onSubmit,
  regulation,
  ready,
  quota,
  inputRef,
}: {
  value: string;
  onChange: (value: string) => void;
  onSubmit: () => void;
  regulation: string;
  /** Loaded and no answer in progress. */
  ready: boolean;
  quota: ChatQuota | undefined;
  inputRef: RefObject<HTMLTextAreaElement | null>;
}) {
  const reached = quota?.remaining === 0;
  const canSend = ready && !reached && value.trim() !== "";

  // Grows with its content, from one line up to six; then it scrolls.
  useLayoutEffect(() => {
    const field = inputRef.current;
    if (!field) return;
    field.style.height = "auto";
    const { lineHeight, paddingTop, paddingBottom } = getComputedStyle(field);
    const borders = field.offsetHeight - field.clientHeight;
    const max =
      MAX_LINES * parseFloat(lineHeight) + parseFloat(paddingTop) + parseFloat(paddingBottom);
    field.style.height = `${Math.min(field.scrollHeight, max) + borders}px`;
  }, [value, inputRef]);

  const submit = () => {
    if (canSend) onSubmit();
  };

  return (
    <div className="px-5 pt-4 pb-4">
      <div className="mx-auto w-full max-w-180">
        {reached && quota && <QuotaNotice quota={quota} />}
        <div className="relative">
          <label htmlFor={COMPOSER_ID} className="sr-only">
            {copy.chat.inputLabel}
          </label>
          <textarea
            id={COMPOSER_ID}
            ref={inputRef}
            rows={1}
            value={value}
            maxLength={MAX_QUESTION_LENGTH}
            placeholder={format(copy.chat.inputPlaceholder, { regulation })}
            onChange={(event) => onChange(event.target.value)}
            onKeyDown={(event) => {
              // Enter sends, Shift+Enter breaks the line, Enter while composing (IME) does neither.
              if (event.key !== "Enter" || event.shiftKey || event.nativeEvent.isComposing) return;
              event.preventDefault();
              submit();
            }}
            className="block min-h-11 w-full resize-none rounded-control border border-border bg-inset py-2.5 pr-12 pl-3.5 text-field placeholder:text-text-muted sm:text-body"
          />
          {/* aria-disabled, not disabled: it stays focusable and can point at the notice. */}
          <button
            type="button"
            aria-label={copy.chat.send}
            aria-disabled={!canSend}
            aria-describedby={reached ? NOTICE_ID : undefined}
            onClick={() => {
              submit();
              inputRef.current?.focus();
            }}
            className="absolute right-1.5 bottom-1.5 flex size-8 items-center justify-center rounded-pill bg-mint text-on-mint hover:bg-mint-hover aria-disabled:opacity-50 aria-disabled:hover:bg-mint"
          >
            <SendHorizontal aria-hidden="true" className="size-4" />
          </button>
        </div>
        {quota?.limit != null && quota.remaining !== null && (
          <p className="mt-2 text-caption text-text-secondary tabular-nums">
            {format(copy.quota.remaining, {
              remaining: String(quota.remaining),
              limit: String(quota.limit),
            })}
          </p>
        )}
      </div>
    </div>
  );
}

/** At zero: when the user can ask again, kept current every minute; refetched at the reset. */
function QuotaNotice({ quota }: { quota: ChatQuota }) {
  const queryClient = useQueryClient();
  const [now, setNow] = useState(Date.now);
  const resetsAt = Date.parse(quota.resetsAt);

  useEffect(() => {
    const tick = setInterval(() => setNow(Date.now()), 60_000);
    const reset = setTimeout(
      () => void queryClient.invalidateQueries({ queryKey: quotaKey }),
      Math.max(0, resetsAt - Date.now()),
    );
    return () => {
      clearInterval(tick);
      clearTimeout(reset);
    };
  }, [queryClient, resetsAt]);

  const { title, body } = quotaNoticeText(quota, now);
  return (
    <div id={NOTICE_ID} className="mb-3 flex gap-2 rounded-sm bg-inset p-3">
      <Clock aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-text-secondary" />
      <div>
        <p className="text-body font-semibold">{title}</p>
        <p className="text-small text-text-secondary">{body}</p>
      </div>
    </div>
  );
}
