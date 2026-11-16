import type { Sent } from "../settings/profiles";
import type { DocumentDetail, DocumentSummary, DocumentVersion } from "../api/queries";
import { copy, format } from "../copy";

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
 * The file as sent: a browser that knows no type for the extension (a .docx without Office) sends
 * none, which the core refuses, so it gets the one the core expects.
 */
export const withContentType = (file: File) =>
  file.type ? file : new File([file], file.name, { type: formats[extensionOf(file.name)] });

/** Where an upload's refusal shows: on the file (the drop zone) or above the buttons. */
export function uploadRefusal(result: Sent & { ok: false }, fileName: string) {
  const file = { file: fileName };
  switch (result.code) {
    case "document.unsupported_format":
      return { file: format(copy.upload.wrongType, file) };
    case "document.invalid_content":
    case "document.empty":
      return { file: format(copy.upload.invalidContent, file) };
    case "document.too_large":
      return { file: format(copy.upload.tooLarge, file) };
    case "document.limit_reached":
      return { form: copy.upload.limitReached };
    case "document.version_in_flight":
      return { form: copy.upload.inFlight };
  }
  return { form: result.status === 403 ? copy.settings.denied : copy.upload.network };
}

/** Waiting or Processing: a new version is refused until it finishes. */
export const inFlight = ({ status }: DocumentVersion) =>
  status === "Uploaded" || status === "Processing";

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
  const reason = newest.status === "Failed" ? failureText(newest.failureCode).short : undefined;
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
  if (!newest || newest.status === "Indexed") return undefined;
  const n = String(newest.number);
  const c = currentVersionNumber === null ? undefined : String(currentVersionNumber);
  if (newest.status === "Failed")
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
      newest.status === "Processing" ? copy.documents.processingTitle : copy.documents.waitingTitle,
      { n },
    ),
    lines: [c ? format(copy.documents.staysInUse, { c, n }) : copy.documents.readableOnceIndexed],
  };
}

/** A list row from an upload's answer, so it shows before the refetch. */
export const toSummary = ({ versions, ...document }: DocumentDetail): DocumentSummary => ({
  ...document,
  latestVersion: versions[0],
});
