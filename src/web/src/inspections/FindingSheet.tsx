import {
  Check,
  ChevronDown,
  ChevronRight,
  ChevronUp,
  CircleAlert,
  Pencil,
  X,
  type LucideIcon,
} from "lucide-react";
import { Dialog } from "radix-ui";
import { useEffect, useId, useRef, useState, type ReactNode, type Ref } from "react";
import { Link } from "react-router";
import type { RequestOptions } from "../api/client";
import type { EvidenceCitation, Finding, RegulationCitation } from "../api/queries";
import { calendarDate, copy, format, languageName } from "../copy";
import type { Sent } from "../settings/profiles";
import {
  Button,
  FormDialog,
  iconButton,
  QuotedPassage,
  SelectField,
  Spinner,
  StatusPill,
  TextAreaField,
  TextField,
  textAction,
  TimeBadge,
} from "../ui/primitives";
import { pillVariant } from "../ui/statuses";
import {
  confidenceLabel,
  draftOf,
  dueBadge,
  editChanges,
  editErrors,
  editFields,
  originalValues,
  reviewError,
  reviewLabel,
  reviewSentence,
  severityLabel,
  severityOrder,
  themeLabel,
  useReview,
  verdictLabel,
  verdictOrder,
  type EditDraft,
  type EditErrors,
  type Refusal,
} from "./findings";

/** Rejected note and edited texts, as the core limits them. */
const NOTE_MAX_LENGTH = 1000;
const RATIONALE_MAX_LENGTH = 2000;
const ACTION_MAX_LENGTH = 1000;

const text = "text-body whitespace-pre-line";

export const VerdictPill = ({ verdict }: { verdict: string }) => (
  <StatusPill variant={pillVariant("verdict", verdict)} label={verdictLabel(verdict)} />
);

export const ReviewPill = ({ status }: { status: string }) => (
  <StatusPill variant={pillVariant("review", status)} label={reviewLabel(status)} />
);

/** An open action's Time badge ("Due in 7 days"), or the Attention pill "Overdue". */
export function DueBadge({ dueOn }: { dueOn: string }) {
  const { overdue, label } = dueBadge(dueOn);
  return overdue ? (
    <StatusPill variant="attention" label={label} />
  ) : (
    <TimeBadge dateTime={dueOn} label={label} />
  );
}

/**
 * The content of the finding sheet (spec §5 Finding sheet): a header with the position, Previous,
 * Next and Close, the finding with both quotes, and the review footer with `canReview`. The body
 * and footer remount per finding, so the header's buttons keep focus while it moves. Focus goes to
 * the title when the sheet opens.
 */
export function FindingSheet({
  finding,
  n,
  total,
  onPrevious,
  onNext,
  onClose,
  establishmentId,
  inspectionId,
  canReview,
  canReadDocuments,
  send,
  announce,
}: {
  finding: Finding;
  n: number;
  total: number;
  /** Undefined at the first finding. */
  onPrevious: (() => void) | undefined;
  /** Undefined at the last finding. */
  onNext: (() => void) | undefined;
  onClose: () => void;
  establishmentId: string;
  inspectionId: string;
  canReview: boolean;
  canReadDocuments: boolean;
  send: (path: string, options: RequestOptions) => Promise<Sent>;
  /** The sheet's own polite region (the page's is hidden while the sheet is open). */
  announce: (text: string) => void;
}) {
  const heading = useRef<HTMLHeadingElement>(null);
  const reviewId = useId();
  const review = useReview(establishmentId, inspectionId, send);

  useEffect(() => {
    heading.current?.focus();
  }, []);

  return (
    <>
      <header className="flex items-center gap-1 border-b border-border py-3 pr-3 pl-5">
        <p className="flex-1 text-small text-text-secondary tabular-nums">
          {format(copy.findings.position, { n: String(n), total: String(total) })}
        </p>
        <StepButton label={copy.findings.previous} icon={ChevronUp} onClick={onPrevious} />
        <StepButton label={copy.findings.next} icon={ChevronDown} onClick={onNext} />
        <button
          type="button"
          aria-label={copy.findings.close}
          onClick={onClose}
          className={iconButton}
        >
          <X aria-hidden="true" className="size-5" />
        </button>
      </header>
      <FindingBody
        key={`body-${finding.id}`}
        finding={finding}
        headingRef={heading}
        reviewId={reviewId}
        canReadDocuments={canReadDocuments}
      />
      {canReview && (
        <ReviewFooter
          key={`footer-${finding.id}`}
          finding={finding}
          reviewId={reviewId}
          review={review}
          announce={announce}
        />
      )}
    </>
  );
}

/** Previous or Next: `aria-disabled` at either end, so focus stays on it. */
function StepButton({
  label,
  icon: Icon,
  onClick,
}: {
  label: string;
  icon: LucideIcon;
  onClick: (() => void) | undefined;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      aria-disabled={!onClick}
      onClick={onClick}
      className={`${iconButton} aria-disabled:opacity-50 aria-disabled:hover:bg-transparent`}
    >
      <Icon aria-hidden="true" className="size-5" />
    </button>
  );
}

function FindingBody({
  finding,
  headingRef,
  reviewId,
  canReadDocuments,
}: {
  finding: Finding;
  headingRef: Ref<HTMLHeadingElement>;
  reviewId: string;
  canReadDocuments: boolean;
}) {
  const id = useId();
  const action = finding.actionItem;
  const originals = originalValues(finding);
  return (
    <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">
      <Dialog.Title asChild>
        <h2 ref={headingRef} tabIndex={-1} className="text-title font-semibold break-words">
          {finding.title}
        </h2>
      </Dialog.Title>
      <div className="mt-2 flex flex-wrap gap-1.5">
        <VerdictPill verdict={finding.verdict} />
        <ReviewPill status={finding.reviewStatus} />
      </div>
      <dl className="mt-4 grid grid-cols-3 gap-3">
        <Fact term={copy.findings.theme} value={themeLabel(finding.theme)} />
        <Fact term={copy.findings.severity} value={severityLabel(finding.severity)} />
        <Fact term={copy.findings.confidence} value={confidenceLabel(finding.confidence)} />
      </dl>

      <Section title={copy.findings.rationale}>
        <p className={text}>{finding.rationale}</p>
      </Section>

      <Section title={copy.findings.regulation}>
        {finding.regulationCitations.map((citation, index) => (
          <RegulationQuote key={index} citation={citation} first={index === 0} />
        ))}
      </Section>

      <Section title={copy.findings.evidence}>
        {finding.evidenceCitations.length === 0 ? (
          <p className="text-body text-text-secondary">{copy.findings.noEvidence}</p>
        ) : (
          finding.evidenceCitations.map((citation, index) => (
            <EvidenceQuote
              key={index}
              id={`${id}-${index}`}
              citation={citation}
              first={index === 0}
              canReadDocuments={canReadDocuments}
            />
          ))
        )}
        <p className="mt-3 text-caption text-text-secondary">{copy.findings.storedCopy}</p>
      </Section>

      <Section title={copy.findings.action}>
        {action ? (
          <>
            <p className={text}>{action.text}</p>
            <p className="mt-2 flex flex-wrap items-center gap-2 text-body">
              <time dateTime={action.dueOn}>
                {format(copy.findings.due, { date: calendarDate(action.dueOn) })}
              </time>
              {action.status === "Done" ? (
                <StatusPill variant="done" label={copy.actions.doneBadge} />
              ) : (
                <DueBadge dueOn={action.dueOn} />
              )}
            </p>
          </>
        ) : (
          <p className={text}>{copy.findings.noAction}</p>
        )}
      </Section>

      <Section title={copy.findings.reviewHeading}>
        <p id={reviewId} className="text-body">
          {reviewSentence(finding)}
        </p>
        {finding.reviewStatus === "Rejected" && finding.reviewNote && (
          <>
            <p className="mt-3 text-small text-text-secondary">{copy.findings.reason}</p>
            <p className={`mt-1 rounded-sm bg-inset p-3 ${text}`}>{finding.reviewNote}</p>
          </>
        )}
        {originals.length > 0 && (
          <Disclosure summary={copy.findings.original} className="mt-3">
            <dl className="mt-2 flex flex-col gap-2">
              {originals.map((field) => (
                <div key={field.label}>
                  <dt className="text-small text-text-secondary">{field.label}</dt>
                  <dd className={text}>{field.value}</dd>
                </div>
              ))}
            </dl>
          </Disclosure>
        )}
      </Section>
    </div>
  );
}

function Fact({ term, value }: { term: string; value: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-small text-text-secondary">{term}</dt>
      <dd className="text-body break-words">{value}</dd>
    </div>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <>
      <h3 className="mt-5 mb-1.5 text-small font-semibold text-text-secondary">{title}</h3>
      {children}
    </>
  );
}

/** A native `<details>`: Enter or Space opens it; the chevron turns (not under reduced motion). */
function Disclosure({
  summary,
  className,
  children,
}: {
  summary: string;
  className: string;
  children: ReactNode;
}) {
  return (
    <details className={`group ${className}`}>
      <summary className="inline-flex cursor-pointer list-none items-center gap-1 rounded-sm text-small font-medium text-mint [&::-webkit-details-marker]:hidden">
        <ChevronRight
          aria-hidden="true"
          className="size-3.5 shrink-0 group-open:rotate-90 motion-safe:transition-transform"
        />
        {summary}
      </summary>
      {children}
    </details>
  );
}

/** The quote alone, then the stored passage with the quote highlighted on request. */
function Quote({ citation }: { citation: RegulationCitation | EvidenceCitation }) {
  return (
    <>
      <QuotedPassage citation={citation} mode="quote" className="mt-2" />
      <Disclosure summary={copy.findings.wholePassage} className="mt-2">
        <QuotedPassage citation={citation} mode="passage" className="mt-2" />
      </Disclosure>
    </>
  );
}

function RegulationQuote({ citation, first }: { citation: RegulationCitation; first: boolean }) {
  return (
    <div className={first ? "" : "mt-4"}>
      <p className="text-small text-text-secondary">
        {format(copy.findings.regulationMeta, {
          source: citation.sourceTitle,
          section: citation.sectionPath,
        })}
      </p>
      <p className="text-caption text-text-secondary tabular-nums">
        {format(copy.findings.sourceMeta, {
          version: citation.sourceVersion,
          language: languageName(citation.language),
        })}
      </p>
      <Quote citation={citation} />
    </div>
  );
}

function EvidenceQuote({
  id,
  citation,
  first,
  canReadDocuments,
}: {
  id: string;
  citation: EvidenceCitation;
  first: boolean;
  canReadDocuments: boolean;
}) {
  return (
    <div className={first ? "" : "mt-4"}>
      <div className="flex items-baseline justify-between gap-3">
        <p id={id} className="min-w-0 text-body font-medium break-words">
          {citation.documentTitle ?? copy.findings.untitledDocument}
        </p>
        {canReadDocuments && (
          // Several documents may be cited: the title in its name tells the links apart.
          <Link
            id={`${id}-link`}
            to={`/documents/${encodeURIComponent(citation.documentId)}`}
            state={{ focusTitle: true }}
            aria-labelledby={`${id}-link ${id}`}
            className={`${textAction} shrink-0`}
          >
            {copy.findings.openDocument}
          </Link>
        )}
      </div>
      <p className="text-caption text-text-secondary tabular-nums">
        {format(copy.findings.sourceMeta, {
          version: String(citation.versionNumber),
          language: languageName(citation.language),
        })}
      </p>
      <Quote citation={citation} />
    </div>
  );
}

type Review = (path: string, options: RequestOptions) => Promise<Refusal | undefined>;

/** Reject and Edit (each with its dialog), then the primary Accept, under an inline error. */
function ReviewFooter({
  finding,
  reviewId,
  review,
  announce,
}: {
  finding: Finding;
  /** The review sentence, which says why Accept is unavailable once accepted. */
  reviewId: string;
  review: Review;
  announce: (text: string) => void;
}) {
  const [accepting, setAccepting] = useState(false);
  const [error, setError] = useState<string>();
  const accepted = finding.reviewStatus === "Accepted";
  const path = `findings/${encodeURIComponent(finding.id)}`;

  const accept = async () => {
    setAccepting(true);
    setError(undefined);
    const refusal = await review(`${path}/accept`, { method: "POST" });
    setAccepting(false);
    if (refusal) setError(reviewError(refusal));
    else announce(copy.findings.accepted);
  };

  return (
    <footer className="border-t border-border px-5 py-4">
      {error && (
        <p role="alert" className="mb-3 flex items-start gap-1.5 text-body">
          <CircleAlert aria-hidden="true" className="size-4 shrink-0 text-text-secondary" />
          {error}
        </p>
      )}
      <div className="flex justify-end gap-2">
        <RejectDialog finding={finding} path={path} review={review} announce={announce} />
        <EditDialog finding={finding} path={path} review={review} announce={announce} />
        {/* aria-disabled, not disabled: focus stays on it after accepting. */}
        <Button
          variant="primary"
          className={`flex-1 aria-disabled:hover:bg-mint sm:flex-none ${accepted ? "opacity-50" : ""}`}
          aria-disabled={accepted || accepting}
          aria-describedby={accepted ? reviewId : undefined}
          onClick={accepted || accepting ? undefined : () => void accept()}
        >
          {accepting ? <Spinner surface="mint" /> : <Check aria-hidden="true" className="size-4" />}
          {accepting ? copy.findings.accepting : copy.findings.accept}
        </Button>
      </div>
    </footer>
  );
}

/**
 * A reason, required. Prefilled with the current one when the finding is already rejected. Its
 * confirmation is announced once the dialog has closed, when the sheet is heard again.
 */
function RejectDialog({
  finding,
  path,
  review,
  announce,
}: {
  finding: Finding;
  path: string;
  review: Review;
  announce: (text: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const [note, setNote] = useState("");
  // A new object per refusal, so the same error twice still moves focus to the field.
  const [fieldError, setFieldError] = useState<{ text: string }>();
  const [error, setError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);
  const saved = useRef(false);
  const field = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (fieldError) field.current?.focus();
  }, [fieldError]);

  const submit = async () => {
    const trimmed = note.trim();
    setError(undefined);
    if (!trimmed) {
      setFieldError({ text: copy.findings.reasonRequired });
      return;
    }
    setSubmitting(true);
    const refusal = await review(`${path}/reject`, { method: "POST", json: { note: trimmed } });
    setSubmitting(false);
    if (refusal) {
      setError(reviewError(refusal));
      return;
    }
    saved.current = true;
    setOpen(false);
  };

  return (
    <FormDialog
      trigger={
        <Button variant="secondary" className="flex-1 sm:flex-none">
          <X aria-hidden="true" className="size-4" />
          {copy.findings.reject}
        </Button>
      }
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) return;
        setNote(finding.reviewStatus === "Rejected" ? (finding.reviewNote ?? "") : "");
        setFieldError(undefined);
        setError(undefined);
      }}
      title={copy.findings.rejectTitle}
      submitLabel={submitting ? copy.findings.rejecting : copy.findings.rejectSubmit}
      cancelLabel={copy.findings.cancel}
      submitting={submitting}
      error={error}
      onSubmit={() => void submit()}
      onCloseAutoFocus={() => {
        if (saved.current) announce(copy.findings.rejected);
        saved.current = false;
      }}
    >
      <TextAreaField
        ref={field}
        label={copy.findings.reason}
        hint={copy.findings.reasonHint}
        error={fieldError?.text}
        value={note}
        maxLength={NOTE_MAX_LENGTH}
        required
        onChange={(event) => {
          setNote(event.target.value);
          setFieldError(undefined);
        }}
      />
    </FormDialog>
  );
}

/**
 * The current values, any of which can change. Only changed fields are sent; Save changes waits
 * for one. On errors focus moves to the first invalid field.
 */
function EditDialog({
  finding,
  path,
  review,
  announce,
}: {
  finding: Finding;
  path: string;
  review: Review;
  announce: (text: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(() => draftOf(finding));
  const [errors, setErrors] = useState<EditErrors>({});
  const [focusRequest, setFocusRequest] = useState<{ field: keyof EditDraft }>();
  const [error, setError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);
  const saved = useRef(false);
  const rationale = useRef<HTMLTextAreaElement>(null);
  const actionText = useRef<HTMLTextAreaElement>(null);
  const dueOn = useRef<HTMLInputElement>(null);
  const changes = editChanges(finding, draft);
  const keepsAction = finding.actionItem !== null;

  useEffect(() => {
    if (!focusRequest) return;
    const fields: Partial<Record<keyof EditDraft, HTMLElement | null>> = {
      rationale: rationale.current,
      actionText: actionText.current,
      dueOn: dueOn.current,
    };
    fields[focusRequest.field]?.focus();
  }, [focusRequest]);

  const change = (key: keyof EditDraft) => (event: { target: { value: string } }) => {
    setDraft({ ...draft, [key]: event.target.value });
    setErrors({ ...errors, [key]: undefined });
  };

  const submit = async () => {
    setError(undefined);
    const found = editErrors(finding, draft);
    const first = editFields.find((key) => found[key]);
    if (first) {
      setErrors(found);
      setFocusRequest({ field: first });
      return;
    }
    setSubmitting(true);
    const refusal = await review(path, { method: "PATCH", json: changes });
    setSubmitting(false);
    if (refusal) {
      setError(reviewError(refusal));
      return;
    }
    saved.current = true;
    setOpen(false);
  };

  return (
    <FormDialog
      trigger={
        <Button variant="secondary" className="flex-1 sm:flex-none">
          <Pencil aria-hidden="true" className="size-4" />
          {copy.findings.edit}
        </Button>
      }
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) return;
        setDraft(draftOf(finding));
        setErrors({});
        setError(undefined);
      }}
      title={copy.findings.editTitle}
      submitLabel={submitting ? copy.findings.saving : copy.findings.save}
      cancelLabel={copy.findings.cancel}
      submitting={submitting}
      ready={Object.keys(changes).length > 0}
      error={error}
      onSubmit={() => void submit()}
      onCloseAutoFocus={() => {
        if (saved.current) announce(copy.findings.edited);
        saved.current = false;
      }}
      wide
    >
      <div className="flex flex-col gap-4">
        <p className="text-body text-text-secondary">{copy.findings.editNote}</p>
        <div className="grid gap-4 sm:grid-cols-2">
          <SelectField
            label={copy.findings.verdict}
            placeholder={copy.findings.chooseVerdict}
            options={verdictOrder.map((code) => ({ value: code, label: verdictLabel(code) }))}
            value={draft.verdict}
            onChange={change("verdict")}
          />
          <SelectField
            label={copy.findings.severity}
            placeholder={copy.findings.chooseSeverity}
            options={severityOrder.map((code) => ({ value: code, label: severityLabel(code) }))}
            value={draft.severity}
            onChange={change("severity")}
          />
        </div>
        <TextAreaField
          ref={rationale}
          label={copy.findings.rationale}
          error={errors.rationale}
          value={draft.rationale}
          maxLength={RATIONALE_MAX_LENGTH}
          required
          onChange={change("rationale")}
        />
        <TextAreaField
          ref={actionText}
          label={copy.findings.action}
          hint={copy.findings.actionHint}
          error={errors.actionText}
          value={draft.actionText}
          maxLength={ACTION_MAX_LENGTH}
          required={keepsAction}
          onChange={change("actionText")}
        />
        <TextField
          ref={dueOn}
          type="date"
          label={copy.findings.dueDate}
          error={errors.dueOn}
          value={draft.dueOn}
          required={keepsAction}
          onChange={change("dueOn")}
        />
      </div>
    </FormDialog>
  );
}
