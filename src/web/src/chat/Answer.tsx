import { CircleAlert, FileText, SearchX, WifiOff, type LucideIcon } from "lucide-react";
import type { ReactNode } from "react";
import type { Citation, Claim } from "../api/chat";
import { sourceTypes } from "../api/contract";
import { copy, format } from "../copy";
import { LogoMark } from "../ui/Logo";
import { media, useMediaQuery } from "../ui/media";
import { Button, CitationChip, Spinner } from "../ui/primitives";
import { answering, chipId, type Turn } from "./answers";
import { useWordReveal } from "./conversation";

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
      <Spinner surface="elevated" />
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
            <Spinner surface="elevated" />
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
          <div className="mt-4 border-t border-inset pt-4">{message}</div>
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

  return (
    <li aria-busy={answering(turn)} className="flex gap-3">
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
  const { words, visible } = useWordReveal(claims, reveal && !reducedMotion);
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
                  {claim.citations.map((citation) => (
                    <ClaimChip
                      key={citation.id}
                      citation={citation}
                      open={citation.id === openCitationId}
                      onOpen={() => onOpenSource(citation)}
                    />
                  ))}
                </div>
              </>
            )}
          </div>
        );
      })}
    </div>
  );
}

/**
 * A claim's citation chip: "View source" with the regulation's section, or the document (its
 * title alone without a section), which says, to screen readers too, that it is the
 * establishment's own text.
 */
function ClaimChip({
  citation,
  open,
  onOpen,
}: {
  citation: Citation;
  open: boolean;
  onOpen: () => void;
}) {
  const section = citation.sectionPath;
  if (citation.sourceType === sourceTypes.regulation)
    return (
      <CitationChip
        id={chipId(citation)}
        label={format(copy.chat.viewSource, { section })}
        expanded={open}
        aria-controls={open ? SOURCE_PANEL_ID : undefined}
        onClick={onOpen}
      />
    );
  const title = citation.documentTitle ?? copy.chat.untitledDocumentChip;
  const label = section ? format(copy.chat.documentChip, { title, section }) : title;
  return (
    <CitationChip
      id={chipId(citation)}
      icon={FileText}
      label={title}
      detail={section ? format(copy.chat.documentChipSection, { section }) : undefined}
      aria-label={format(copy.chat.documentChipName, { label })}
      expanded={open}
      aria-controls={open ? SOURCE_PANEL_ID : undefined}
      onClick={onOpen}
    />
  );
}
