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
