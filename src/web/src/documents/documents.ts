import type { QueryClient } from "@tanstack/react-query";
import { documentStatuses, problemCodes } from "../api/contract";
import {
  documentsKey,
  type DocumentDetail,
  type DocumentSummary,
  type DocumentVersion,
} from "../api/documents";
import type { Sent } from "../api/send";
import { calendarDate, copy, format, localDate, relativeDays } from "../copy";

/** The list's expiry filter: every document, the expired ones, or those expiring soon. */
export type ExpiryFilter = "" | "expired" | "expiring";

/** "Expiring soon" (spec 004): the Time badge's window and the list's filter. */
export const EXPIRING_DAYS = 30;

/** The core's documents hub and its event (spec §5). */
export const HUB_URL = "/hubs/documents";
export const HUB_EVENT = "DocumentStatusChanged";

/** What the hub pushes for each status change of a version (DocumentStatusChange in the core). */
export interface DocumentStatusChange {
  establishmentId: string;
  documentId: string;
  versionId: string;
  versionNumber: number;
  status: string;
  failureCode: string | null;
  failureReason: string | null;
  currentVersionNumber: number | null;
  changedAt: string;
}

/** DocumentFiles.MaxSize in the core: 10 MiB, the AI service's limit too. */
export const MAX_FILE_SIZE = 10 * 1024 * 1024;

/** Document.TitleMaxLength in the core. */
export const TITLE_MAX_LENGTH = 200;

/** What the core accepts, by extension, with the content type it expects for each. */
const formats: Record<string, string> = {
  ".pdf": "application/pdf",
  ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  ".txt": "text/plain",
  ".csv": "text/csv",
};

export const ACCEPT = Object.keys(formats).join(",");

const extensionOf = (name: string) => /\.[^.]*$/.exec(name.toLowerCase())?.[0] ?? "";

/** The name without its extension, to prefill the title. */
export const titleFromFile = (name: string) =>
  name.replace(/\.[^.]*$/, "").slice(0, TITLE_MAX_LENGTH);

/**
 * Why these files can't be uploaded, or undefined for one acceptable file. The checks the browser
 * can make of the server's (extension, empty, size); the page count needs the worker.
 */
export function fileProblem(files: File[]): string | undefined {
  if (files.length > 1) return copy.upload.oneFile;
  const [file] = files;
  if (!file) return copy.upload.fileRequired;
  if (!(extensionOf(file.name) in formats))
    return format(copy.upload.wrongType, { file: file.name });
  if (file.size === 0) return format(copy.upload.invalidContent, { file: file.name });
  if (file.size > MAX_FILE_SIZE) return format(copy.upload.tooLarge, { file: file.name });
  return undefined;
}

/**
 * The file with the content type the core expects for its extension. Browsers report their own: none
 * for a .docx without Office, application/octet-stream or text/x-csv on some systems. The core
 * checks the file's first bytes anyway.
 */
export const withContentType = (file: File) => {
  const type = formats[extensionOf(file.name)];
  return !type || file.type === type ? file : new File([file], file.name, { type });
};

/** Where an upload's refusal shows: on the file (the drop zone) or above the buttons. */
export function uploadRefusal(result: Sent & { ok: false }, fileName: string) {
  const file = { file: fileName };
  switch (result.code) {
    case problemCodes.unsupportedFormat:
      return { file: format(copy.upload.wrongType, file) };
    case problemCodes.invalidContent:
    case problemCodes.emptyDocument:
      return { file: format(copy.upload.invalidContent, file) };
    case problemCodes.tooLarge:
      return { file: format(copy.upload.tooLarge, file) };
    case problemCodes.documentLimitReached:
      return { form: copy.upload.limitReached };
    case problemCodes.versionLimitReached:
      return { form: copy.upload.versionLimitReached };
    case problemCodes.versionInFlight:
      return { form: copy.upload.inFlight };
  }
  return { form: result.status === 403 ? copy.settings.denied : copy.upload.network };
}

/** Waiting or Processing: a new version is refused until it finishes. */
export const inFlight = ({ status }: DocumentVersion) =>
  status === documentStatuses.uploaded || status === documentStatuses.processing;

export const statusLabel = (status: string) => copy.documents.statuses[status] ?? status;

export const typeLabel = (type: string) => copy.documents.types[type] ?? type;

export const failureText = (code: string | null) =>
  copy.documents.failures[code ?? ""] ?? copy.documents.failureUnknown;

/** "v3" for the eye, "Version 3" for the ear. */
export const versionText = (n: number) => ({
  short: format(copy.documents.versionShort, { n: String(n) }),
  long: format(copy.documents.versionLong, { n: String(n) }),
});

/**
 * The caption under a list row's pill (spec §4): the newest version when it isn't the current
 * one (there is a current one), the failure's short reason, or both.
 */
export function rowCaption({
  latestVersion: newest,
  currentVersionNumber: current,
}: DocumentSummary): string | undefined {
  const n = String(newest.number);
  const reason =
    newest.status === documentStatuses.failed ? failureText(newest.failureCode).short : undefined;
  if (current === null || newest.number === current) return reason;
  return reason
    ? format(copy.documents.versionAndReason, { n, reason })
    : format(copy.documents.versionLong, { n });
}

/**
 * The detail's notice about the newest version (spec §4), or undefined once it is indexed. Each
 * line is its own paragraph.
 */
export function versionNotice({ versions, currentVersionNumber }: DocumentDetail) {
  const [newest] = versions;
  if (!newest || newest.status === documentStatuses.indexed) return undefined;
  const n = String(newest.number);
  const c = currentVersionNumber === null ? undefined : String(currentVersionNumber);
  if (newest.status === documentStatuses.failed)
    return {
      status: newest.status,
      title: format(copy.documents.failedTitle, { n }),
      lines: [
        failureText(newest.failureCode).long,
        ...(c ? [format(copy.documents.stillInUse, { c })] : []),
      ],
    };
  return {
    status: newest.status,
    title: format(
      newest.status === documentStatuses.processing
        ? copy.documents.processingTitle
        : copy.documents.waitingTitle,
      { n },
    ),
    lines: [c ? format(copy.documents.staysInUse, { c, n }) : copy.documents.readableOnceIndexed],
  };
}

/**
 * An expiry date's state (spec "Expiry dates"), in local dates: Expired before today, a Time badge
 * from today to `EXPIRING_DAYS` ahead ("Expires today", "Expires in 10 days"), the date after.
 */
export function expiry(expiresOn: string, now = new Date()) {
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  // Rounded: a day across a daylight saving change is 23 or 25 hours.
  const days = Math.round((localDate(expiresOn).getTime() - today.getTime()) / 86_400_000);
  if (days < 0) return { state: "expired", label: copy.documents.expired } as const;
  if (days <= EXPIRING_DAYS)
    return {
      state: "soon",
      label: format(copy.documents.expiresIn, { when: relativeDays(days) }),
    } as const;
  return { state: "later", label: calendarDate(expiresOn, now) } as const;
}

/**
 * Why a date field's value can't be sent, or undefined ("" means no date). A half-typed date
 * reads as "" in the browser, which would clear the date, so `badInput` catches it.
 */
export const expiryProblem = (input: HTMLInputElement | null) =>
  input && (input.validity.badInput || (input.value && !/^\d{4}-\d{2}-\d{2}$/.test(input.value)))
    ? copy.upload.expiryInvalid
    : undefined;

/** A document's title from the establishment's cached list, if it is there. */
export const cachedTitle = (queryClient: QueryClient, establishmentId: string, id: string) =>
  queryClient
    .getQueryData<DocumentSummary[]>(documentsKey(establishmentId))
    ?.find((row) => row.id === id)?.title;

/** A list row from an upload's answer, so it shows before the refetch. */
export const toSummary = ({ versions, ...document }: DocumentDetail): DocumentSummary => ({
  ...document,
  latestVersion: versions[0],
});
