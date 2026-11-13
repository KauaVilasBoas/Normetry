import { CircleAlert, SearchX, WifiOff, type LucideIcon } from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";
import type { Citation, Claim } from "../api/queries";
import { copy, format } from "../copy";
import { LogoMark } from "../ui/Logo";
import { media, useMediaQuery } from "../ui/media";
import { Button, CitationChip, Spinner } from "../ui/primitives";
import { chipId, type Turn } from "./answers";

/** About 25 words per second (DESIGN.md §6). */
const WORD_MS = 40;
export const SOURCE_PANEL_ID = "source-panel";

interface AnswerProps {
  turn: Turn;
  regulation: string;
  openCitationId: string | undefined;
  onOpenSource: (citation: Citation) => void;
  /** Only on the latest turn asked in this view, once it failed. */
  onRetry?: () => void;
  /** The plan is limited, so Try again costs a message. */
  retryCosts: boolean;
}

/** The assistant's side of a turn, inside its `<li>`: N mark and bubble. */
export function Answer({
  turn,
  regulation,
  openCitationId,
  onOpenSource,
  onRetry,
  retryCosts,
}: AnswerProps) {
  const claims = turn.claims.length > 0 && (
    <Claims
      key={turn.attempt}
      claims={turn.claims}
      language={turn.language}
      reveal={turn.live}
      openCitationId={openCitationId}
      onOpenSource={onOpenSource}
    />
  );
  const retry = onRetry && (
    <>
      <Button variant="secondary" onElevated className="mt-2" onClick={onRetry}>
        {copy.chat.tryAgain}
      </Button>
      {retryCosts && <p className="mt-2 text-caption text-text-secondary">{copy.chat.retryCost}</p>}
    </>
  );
  const refusal = (
    <SystemMessage
      icon={SearchX}
      line={format(copy.chat.refusal, { regulation })}
      hint={copy.chat.refusalHint}
    />
  );
  const failure = (icon: LucideIcon, line: string, hint?: string) => (
    <SystemMessage icon={icon} line={line} hint={hint} alert={turn.live} action={retry} />
  );

  const checking = (
    <p className="flex items-center gap-2 text-body text-text-secondary">
      <Spinner />
      {format(copy.chat.checking, { regulation })}
    </p>
  );

  let body: ReactNode;
  switch (turn.status) {
    case "pending":
      body = checking;
      break;
    case "streaming":
      // Still checking until the first claim; then a lone spinner under the last one.
      body = claims ? (
        <>
          {claims}
          <span className="mt-3 block">
            <Spinner />
          </span>
        </>
      ) : (
        checking
      );
      break;
    case "refusal":
      body = refusal;
      break;
    case "unavailable":
    case "network":
    case "error": {
      const message =
        turn.status === "unavailable"
          ? failure(CircleAlert, copy.chat.unavailable, copy.chat.unavailableHint)
          : turn.status === "error"
            ? failure(CircleAlert, copy.chat.genericError)
            : failure(WifiOff, claims ? copy.chat.interrupted : copy.chat.networkError);
      body = claims ? (
        <>
          {claims}
          <div className="mt-4 border-t border-border pt-4">{message}</div>
        </>
      ) : (
        message
      );
      break;
    }
    default:
      // Done: a stored or streamed answer with nothing left to show is a refusal (no claim
      // without a citation reaches the screen).
      body = claims || refusal;
  }

  const busy = turn.status === "pending" || turn.status === "streaming";
  return (
    <li aria-busy={busy} className="flex gap-3">
      <span
        aria-hidden="true"
        className="flex size-8 shrink-0 items-center justify-center rounded-pill bg-elevated"
      >
        <LogoMark className="size-4.5" />
      </span>
      <div className="min-w-0 flex-1 rounded-card border border-border bg-elevated p-4">
        <span className="sr-only">{copy.chat.assistant}</span>
        {body}
      </div>
    </li>
  );
}

function SystemMessage({
  icon: Icon,
  line,
  hint,
  action,
  alert = false,
}: {
  icon: LucideIcon;
  line: string;
  hint?: string;
  action?: ReactNode;
  alert?: boolean;
}) {
  return (
    <div role={alert ? "alert" : undefined} className="flex gap-2">
      <Icon aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-text-secondary" />
      <div className="flex flex-col items-start">
        <p className="text-body font-medium">{line}</p>
        {hint && <p className="text-small text-text-secondary">{hint}</p>}
        {action}
      </div>
    </div>
  );
}

/**
 * Claims in order, each with its chips. A live answer reveals word by word, one claim after the
 * other; the partial text is hidden from screen readers, which get the whole claim beside it.
 * Chips appear once their claim is complete. Under reduced motion, or from history, all at once.
 */
function Claims({
  claims,
  language,
  reveal,
  openCitationId,
  onOpenSource,
}: {
  claims: Claim[];
  language: string | undefined;
  reveal: boolean;
  openCitationId: string | undefined;
  onOpenSource: (citation: Citation) => void;
}) {
  const reducedMotion = useMediaQuery(media.reducedMotion);
  const animate = reveal && !reducedMotion;
  const words = claims.map((claim) => claim.text.split(" "));
  const total = words.reduce((sum, list) => sum + list.length, 0);
  const [shown, setShown] = useState(0);

  useEffect(() => {
    if (!animate || shown >= total) return;
    const next = setTimeout(() => setShown((count) => count + 1), WORD_MS);
    return () => clearTimeout(next);
  }, [animate, shown, total]);

  // Words shown per claim: the reveal fills each claim before the next one starts.
  const visible: number[] = [];
  let left = animate ? shown : Infinity;
  for (const list of words) {
    visible.push(Math.min(list.length, left));
    left -= visible.at(-1)!;
  }
  return (
    <div className="flex flex-col gap-3">
      {claims.map((claim, index) => {
        // Queued behind a claim still revealing; the live region already read it.
        if (index > 0 && visible[index - 1] < words[index - 1].length) return null;
        const revealing = visible[index] < words[index].length;
        return (
          <div key={index}>
            {revealing ? (
              <p lang={language} className="text-body">
                <span aria-hidden="true">{words[index].slice(0, visible[index]).join(" ")}</span>
                <span className="sr-only">{claim.text}</span>
              </p>
            ) : (
              <>
                <p lang={language} className="text-body">
                  {claim.text}
                </p>
                <div className="mt-2 flex flex-wrap gap-2">
                  {claim.citations.map((citation) => {
                    const open = citation.id === openCitationId;
                    return (
                      <CitationChip
                        key={citation.id}
                        id={chipId(citation)}
                        label={format(copy.chat.viewSource, { section: citation.sectionPath })}
                        expanded={open}
                        aria-controls={open ? SOURCE_PANEL_ID : undefined}
                        onClick={() => onOpenSource(citation)}
                      />
                    );
                  })}
                </div>
              </>
            )}
          </div>
        );
      })}
    </div>
  );
}
