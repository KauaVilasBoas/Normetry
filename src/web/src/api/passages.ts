/**
 * A copy of a cited passage, stored with what cites it (an answer, ADR 007, or a finding) so it
 * opens without a fetch; quoted in its own language.
 */
export interface StoredPassage {
  language: string;
  sectionPath: string;
  text: string;
  quote: string;
  /** `text.slice(quoteStart, quoteEnd)` is the quote; both null when it could not be mapped. */
  quoteStart: number | null;
  quoteEnd: number | null;
}

/** The quote's offsets map onto `text`, so it can be highlighted there. */
export const quoteFound = ({
  text,
  quoteStart: start,
  quoteEnd: end,
}: Pick<StoredPassage, "text" | "quoteStart" | "quoteEnd">) =>
  start !== null && end !== null && start >= 0 && start < end && end <= text.length;
