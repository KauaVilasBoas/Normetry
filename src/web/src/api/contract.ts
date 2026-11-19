// The values the API sends and the app compares against, each named once. Screens use these
// objects, never the string itself; the types that need a value derive it from here.

/** The permission catalog, as `Normetry.Domain/Identity/Permissions.cs` names it. */
export const permissions = {
  establishments: { read: "establishments.read", manage: "establishments.manage" },
  documents: { read: "documents.read", upload: "documents.upload", delete: "documents.delete" },
  chat: { ask: "chat.ask" },
  inspections: { read: "inspections.read", run: "inspections.run", review: "inspections.review" },
  profiles: { read: "profiles.read", manage: "profiles.manage" },
} as const;

/** The `code` of a problem response the app reacts to (`problemCode` reads it). */
export const problemCodes = {
  validation: "validation",
  rateLimited: "rate_limited",
  unsupportedFormat: "document.unsupported_format",
  invalidContent: "document.invalid_content",
  emptyDocument: "document.empty",
  tooLarge: "document.too_large",
  documentLimitReached: "document.limit_reached",
  versionLimitReached: "document.version_limit_reached",
  versionInFlight: "document.version_in_flight",
  inspectionQuota: "inspection_quota",
  inspectionRunning: "inspection_running",
  inspectionCapacity: "inspection_capacity",
  noIndexedDocuments: "no_indexed_documents",
  lastOwner: "profile.last_owner",
  nameTaken: "profile.name_taken",
} as const;
