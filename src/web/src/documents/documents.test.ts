import { describe, expect, it } from "vitest";
import type { DocumentDetail, DocumentSummary, DocumentVersion } from "../api/queries";
import { fileSize } from "../copy";
import {
  expiry,
  fileProblem,
  MAX_FILE_SIZE,
  rowCaption,
  titleFromFile,
  uploadRefusal,
  versionNotice,
  withContentType,
} from "./documents";

const file = (name: string, size = 10, type = "") => {
  const made = new File(["x"], name, { type });
  Object.defineProperty(made, "size", { value: size });
  return made;
};

const version = (number: number, status: string, failureCode: string | null = null) =>
  ({ number, status, failureCode }) as DocumentVersion;

const row = (current: number | null, newest: DocumentVersion) =>
  ({ currentVersionNumber: current, latestVersion: newest }) as DocumentSummary;

const detail = (current: number | null, ...versions: DocumentVersion[]) =>
  ({ currentVersionNumber: current, versions }) as DocumentDetail;

describe("file checks", () => {
  it("accepts one PDF, DOCX, TXT or CSV file of up to 10 MiB, in any case", () => {
    expect(fileProblem([file("SOP.PDF")])).toBeUndefined();
    expect(fileProblem([file("log.csv", MAX_FILE_SIZE)])).toBeUndefined();
    expect(fileProblem([])).toBe("Choose a file to upload.");
    expect(fileProblem([file("a.pdf"), file("b.pdf")])).toBe("Drop one file at a time.");
    expect(fileProblem([file("photo.png")])).toBe(
      "“photo.png” isn't a PDF, DOCX, TXT or CSV file.",
    );
    expect(fileProblem([file("pdf")])).toBe("“pdf” isn't a PDF, DOCX, TXT or CSV file.");
    expect(fileProblem([file("big.pdf", MAX_FILE_SIZE + 1)])).toBe(
      "“big.pdf” is larger than 10 MB.",
    );
    expect(fileProblem([file("empty.txt", 0)])).toBe(
      "“empty.txt” isn't a valid PDF, DOCX, TXT or CSV file.",
    );
  });

  it("sends the content type the core expects for the extension, whatever the browser says", () => {
    expect(withContentType(file("sop.docx")).type).toBe(
      "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    );
    expect(withContentType(file("sop.docx", 10, "application/octet-stream")).type).toBe(
      "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    );
    expect(withContentType(file("log.csv", 10, "text/x-csv")).type).toBe("text/csv");
    expect(withContentType(file("log.csv", 10, "application/vnd.ms-excel")).type).toBe("text/csv");
  });

  it("shows a full document's refusal above the buttons", () => {
    const refused = { ok: false, status: 409, code: "document.version_limit_reached" } as const;
    expect(uploadRefusal(refused, "sop.pdf")).toEqual({
      form: "This document has reached its version limit. Upload the file as a new document.",
    });
  });

  it("prefills the title without the extension", () => {
    expect(titleFromFile("cold-storage-sop.v2.pdf")).toBe("cold-storage-sop.v2");
  });

  it("formats sizes in kB below 1 MB and in MB from there", () => {
    expect(fileSize(2_516_582)).toBe("2.4 MB");
    expect(fileSize(850_000)).toBe("830.1 kB");
  });
});

describe("status rules", () => {
  it("captions the version in flight only when another one is current, and a failure's reason", () => {
    expect(rowCaption(row(3, version(3, "Indexed")))).toBeUndefined();
    expect(rowCaption(row(4, version(5, "Processing")))).toBe("Version 5");
    expect(rowCaption(row(null, version(1, "Uploaded")))).toBeUndefined();
    expect(rowCaption(row(null, version(1, "Failed", "no_text_layer")))).toBe("No text layer");
    expect(rowCaption(row(2, version(3, "Failed", "timed_out")))).toBe("Version 3 · Timed out");
    expect(rowCaption(row(null, version(1, "Failed", "something_new")))).toBe(
      "Couldn't be indexed",
    );
  });

  it("explains the newest version until it is indexed, one paragraph per line", () => {
    expect(versionNotice(detail(3, version(3, "Indexed")))).toBeUndefined();
    expect(versionNotice(detail(3, version(4, "Processing"), version(3, "Indexed")))).toEqual({
      status: "Processing",
      title: "Version 4 is processing.",
      lines: ["Version 3 stays in use until version 4 is indexed."],
    });
    expect(versionNotice(detail(null, version(1, "Uploaded")))).toEqual({
      status: "Uploaded",
      title: "Version 1 is waiting to be processed.",
      lines: ["Normetry can read this document once it's indexed."],
    });
    expect(
      versionNotice(detail(1, version(2, "Failed", "too_many_pages"), version(1, "Indexed"))),
    ).toEqual({
      status: "Failed",
      title: "Version 2 couldn't be indexed.",
      lines: [
        "This document has more than 100 pages. Split it and upload each part as its own document.",
        "Version 1 is still in use.",
      ],
    });
  });
});

describe("expiry dates", () => {
  const now = new Date(2026, 10, 16, 15, 30);

  it("is expired only after its last day, and expiring up to 30 days ahead", () => {
    expect(expiry("2026-11-15", now)).toEqual({ state: "expired", label: "Expired" });
    expect(expiry("2026-11-16", now)).toEqual({ state: "soon", label: "Expires today" });
    expect(expiry("2026-11-17", now)).toEqual({ state: "soon", label: "Expires tomorrow" });
    expect(expiry("2026-12-16", now)).toEqual({ state: "soon", label: "Expires in 30 days" });
    expect(expiry("2026-12-17", now)).toEqual({ state: "later", label: "Dec 17" });
    expect(expiry("2027-01-04", now)).toEqual({ state: "later", label: "Jan 4, 2027" });
  });

  it("counts whole days across a daylight saving change", () => {
    expect(expiry("2026-04-02", new Date(2026, 2, 26, 12))).toMatchObject({
      label: "Expires in 7 days",
    });
  });
});
