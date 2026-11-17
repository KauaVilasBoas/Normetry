// Every interface string, in English for now (PT comes with the language switch). Whole sentences
// only, with {placeholders}; never build a sentence by concatenation.

/** The interface locale, for every `Intl` formatter (dates, relative times, language names). */
export const locale = "en";

export const copy = {
  tagline: "Understand Regulations. Stay Ahead.",
  titles: {
    signingIn: "Signing in · Normetry",
    signedOut: "Signed out · Normetry",
    app: "Ask the Regulation · Normetry",
    profiles: "Profiles · Settings · Normetry",
    documents: "Documents · Normetry",
    document: "{title} · Documents · Normetry",
  },
  auth: {
    checking: "Signing you in…",
    redirecting: "Redirecting to sign in…",
    expired: "Your session expired. Redirecting to sign in…",
    signingOut: "Signing you out…",
    failedTitle: "Sign-in didn't complete",
    failedBody:
      "Something went wrong while signing you in. Try again, and if it keeps happening, contact your administrator.",
    unreachableTitle: "Can't reach the sign-in service",
    unreachableBody: "Check your connection and try again.",
    signedOutTitle: "You're signed out",
    signedOutBody: "Sign in to continue to Normetry.",
    tryAgain: "Try again",
    signIn: "Sign in",
  },
  shell: {
    skipLink: "Skip to main content",
    navLabel: "Main",
    askTheRegulation: "Ask the Regulation",
    documents: "Documents",
    settings: "Settings",
    navigation: "Navigation",
    loadingNavigation: "Loading navigation…",
    openNavigation: "Open navigation",
    closeNavigation: "Close navigation",
    organization: "Organization:",
    accountMenu: "Account menu, {name}",
    accountMenuUnnamed: "Account menu",
    signOut: "Sign out",
  },
  access: {
    noneTitle: "You don't have access yet",
    noneBody:
      "Ask an administrator in your organization to give you a profile, then reload this page.",
    noScreenTitle: "Your profile doesn't include any screens yet",
    noScreenBody: "Ask an administrator in your organization to add permissions to your profile.",
    refusedTitle: "You don't have access to {screen}",
    refusedBody: "Ask an administrator in your organization for access.",
    establishmentsTitle: "You don't have access to establishments",
    establishmentsBody:
      "Documents and Ask the Regulation work per establishment. Ask an administrator in your organization for access.",
  },
  settings: {
    sections: "Settings sections",
    profilesTab: "Profiles",
    denied: "You no longer have permission to do this.",
    tryAgain: "Try again",
    cancel: "Cancel",
  },
  profiles: {
    title: "Profiles",
    newProfile: "New profile",
    system: "System",
    allPermissions: "All permissions",
    onePermission: "1 permission",
    permissions: "{count} permissions",
    noPermissions: "No permissions",
    loading: "Loading profiles…",
    errorTitle: "Couldn't load profiles",
    errorBody: "Check your connection and try again.",
    ownerNote:
      "Owner has every permission, including ones added later. It can't be changed or deleted.",
    systemCaption: "System profile. You can change its permissions, but you can't delete it.",
    readOnlyCaption: "You can view profiles but not change them.",
    rename: "Rename profile",
    delete: "Delete profile",
    unsaved: "Unsaved changes",
    save: "Save changes",
    saving: "Saving…",
    saved: "{name} saved.",
    saveError: "Couldn't save {name}. Try again.",
    granted: "Granted:",
    notGranted: "Not granted:",
    notFoundTitle: "This profile isn't available",
    notFoundBody: "It may have been deleted.",
    backToProfiles: "Back to profiles",
    newTitle: "New profile",
    renameTitle: "Rename profile",
    nameLabel: "Name",
    nameHint: "For example, Kitchen lead.",
    create: "Create profile",
    creating: "Creating…",
    renameSave: "Save",
    renameSaving: "Saving…",
    nameRequired: "Enter a name.",
    nameTaken: "A profile named “{name}” already exists.",
    nameError: "Couldn't save the profile. Try again.",
    created: "“{name}” created. Choose its permissions.",
    renamed: "Profile renamed to “{name}”.",
    deleteTitle: "Delete “{name}”?",
    deleteBody: "People who hold this profile lose its permissions. This can't be undone.",
    deleteConfirm: "Delete",
    deleteError: "Couldn't delete the profile. Try again.",
    deleted: "“{name}” deleted.",
  },
  /** Permission areas (the prefix before the dot); an unknown area shows as sent. */
  areas: {
    establishments: "Establishments",
    documents: "Documents",
    inspections: "Simulated Inspection",
    chat: "Ask the Regulation",
    profiles: "Profiles",
  } as Record<string, string | undefined>,
  /** In editor order; an unknown permission shows its name as sent, with no description. */
  permissions: {
    "establishments.read": {
      label: "View establishments",
      description: "See the organization's establishments.",
    },
    "establishments.manage": {
      label: "Manage establishments",
      description: "Add, edit and remove establishments.",
    },
    "documents.read": {
      label: "View documents",
      description: "See documents, their versions and their status.",
    },
    "documents.upload": {
      label: "Upload documents",
      description: "Upload documents and new versions.",
    },
    "documents.delete": {
      label: "Delete documents",
      description: "Delete documents and all their versions.",
    },
    "inspections.read": {
      label: "View inspections",
      description: "See inspections, their progress, findings and action plans.",
    },
    "inspections.run": {
      label: "Run inspections",
      description: "Start a simulated inspection of an establishment.",
    },
    "inspections.review": {
      label: "Review findings",
      description: "Accept, reject or edit findings, and mark actions done.",
    },
    "chat.ask": {
      label: "Ask questions",
      description: "Ask about the regulation that applies to an establishment.",
    },
    "profiles.read": {
      label: "View profiles",
      description: "See profiles, their permissions and who holds them.",
    },
    "profiles.manage": {
      label: "Manage profiles",
      description: "Create, change and delete profiles, and give them to people.",
    },
  } as Record<string, { label: string; description: string } | undefined>,
  users: {
    title: "Users",
    caption: "People appear here after their first sign-in to Normetry.",
    you: "{name} (you)",
    noProfile: "No profile",
    menu: "Profiles",
    menuLabel: "Change profiles for {name}",
    menuTitle: "Profiles for {name}",
    lastOwner:
      "{name} is the organization's last Owner. Give the Owner profile to someone else first.",
    error: "Couldn't change {name}'s profiles. Try again.",
    loading: "Loading users…",
    errorTitle: "Couldn't load users",
    errorBody: "Check your connection and try again.",
  },
  switcher: {
    none: "No establishment",
    name: "Establishment",
    selected: "{name} selected",
    loading: "Loading establishments…",
  },
  picker: {
    title: "Establishments",
    loading: "Loading establishments…",
    meta: "{type} · {jurisdiction} · {regulation}",
    metaUnknownJurisdiction: "{type} · {jurisdiction}",
    emptyTitle: "No establishments yet",
    emptyBody: "Establishments your organization adds to Normetry appear here.",
    errorTitle: "Couldn't load establishments",
    errorBody: "Check your connection and try again.",
    noTenantTitle: "Your account isn't linked to an organization",
    noTenantBody:
      "Ask your administrator to add you to an organization in Normetry, then sign in again.",
    tryAgain: "Try again",
  },
  conversations: {
    title: "Conversations",
    newConversation: "New conversation",
    show: "Show conversations",
    close: "Close conversations",
    loading: "Loading conversations…",
    emptyTitle: "No conversations yet",
    emptyBody: "Questions you ask about {establishment} are kept here.",
    errorTitle: "Couldn't load conversations",
    errorBody: "Check your connection and try again.",
    tryAgain: "Try again",
    untitled: "Untitled conversation",
  },
  chat: {
    newTitle: "New conversation",
    meta: "{establishment} · {regulation}",
    emptyTitle: "Ask about {regulation}",
    emptyBody: "Every statement in an answer links to the passage it comes from.",
    inputLabel: "Your question",
    inputPlaceholder: "Ask a question about {regulation}…",
    send: "Send",
    messages: "Messages",
    you: "You:",
    assistant: "Normetry:",
    checking: "Checking {regulation}…",
    answerComplete: "Answer complete.",
    viewSource: "View source · {section}",
    refusal: "There isn't enough basis in {regulation} to answer this question.",
    refusalHint: "Try rephrasing it or asking about a specific requirement.",
    unavailable: "The assistant is unavailable right now.",
    unavailableHint: "Try again in a few minutes.",
    networkError: "Couldn't reach Normetry. Check your connection and try again.",
    genericError: "Couldn't get an answer. Try again.",
    interrupted: "The connection dropped before the answer finished.",
    tryAgain: "Try again",
    retryCost: "Trying again uses one of today's messages.",
    loading: "Loading conversation…",
    loadErrorTitle: "Couldn't load this conversation",
    loadErrorBody: "Check your connection and try again.",
    notFoundTitle: "This conversation isn't available",
    notFoundBody: "It may have been deleted.",
    startNew: "Start a new conversation",
    deleteConversation: "Delete conversation",
    deleteTitle: "Delete this conversation?",
    deleteBody: "“{title}” and its answers will be deleted. This can't be undone.",
    deleteConfirm: "Delete",
    deleteCancel: "Cancel",
    deleteError: "Couldn't delete the conversation. Try again.",
    deleted: "Conversation deleted.",
  },
  quota: {
    remaining: "{remaining} of {limit} messages left today",
    reachedTitle: "You've used all of today's messages",
    reachedBody: "Your plan includes {limit} messages a day. You can ask again {when}.",
    reachedBodyNoLimit: "You can ask again {when}.",
  },
  source: {
    title: "Source",
    close: "Close source",
    meta: "Version {version} · {language}",
    quoteStart: "Quoted passage:",
    quoteEnd: "End of quoted passage.",
    quoteLabel: "Quoted passage",
    officialPage: "Open the official page",
    newTab: "(opens in a new tab)",
    storedCopy:
      "This is the copy stored with the answer. The official publication is the authoritative text.",
  },
  documents: {
    oneDocument: "1 document",
    count: "{count} documents",
    upload: "Upload document",
    loading: "Loading documents…",
    tableCaption: "Documents for {establishment}",
    document: "Document",
    type: "Type",
    status: "Status",
    version: "Version",
    updated: "Updated",
    file: "File",
    uploadedAt: "Uploaded",
    versionShort: "v{n}",
    versionLong: "Version {n}",
    noVersion: "No indexed version yet",
    versionAndReason: "Version {n} · {reason}",
    emptyTitle: "No documents yet",
    emptyBody:
      "Upload SOPs, licenses, logs and reports for {establishment}. PDF, DOCX, TXT or CSV, up to 10 MB.",
    emptyBodyReadOnly: "Documents uploaded for {establishment} appear here.",
    errorTitle: "Couldn't load documents",
    errorBody: "Check your connection and try again.",
    tryAgain: "Try again",
    reconnecting: "Live updates paused. Reconnecting…",
    stopped: "Live updates stopped. Statuses may be out of date.",
    reconnect: "Reconnect",
    back: "Documents",
    meta: "{type} · {establishment}",
    delete: "Delete",
    deleteLabel: "Delete document",
    uploadVersion: "Upload new version",
    versions: "Versions",
    current: "Current",
    unknownUploader: "Unknown user",
    loadingDocument: "Loading document…",
    loadErrorTitle: "Couldn't load this document",
    notFoundTitle: "This document isn't available",
    notFoundBody: "It may have been deleted.",
    backToDocuments: "Back to documents",
    waitingTitle: "Version {n} is waiting to be processed.",
    processingTitle: "Version {n} is processing.",
    failedTitle: "Version {n} couldn't be indexed.",
    staysInUse: "Version {c} stays in use until version {n} is indexed.",
    readableOnceIndexed: "Normetry can read this document once it's indexed.",
    stillInUse: "Version {c} is still in use.",
    uploadAfter: "You can upload another version when this one finishes.",
    deleteTitle: "Delete “{title}”?",
    deleteBody:
      "The document and all its versions are removed from {establishment}. This can't be undone.",
    deleteConfirm: "Delete",
    cancel: "Cancel",
    deleteError: "Couldn't delete the document. Try again.",
    uploaded: "“{title}” uploaded. It's waiting to be processed.",
    versionUploaded: "New version uploaded. It's waiting to be processed.",
    indexed: "“{title}” is indexed.",
    failed: "“{title}” couldn't be indexed: {reason}.",
    versionIndexed: "Version {n} is indexed and in use.",
    versionFailed: "Version {n} couldn't be indexed: {reason}.",
    deleted: "“{title}” deleted.",
    /** Pill labels by API status; an unknown status shows as sent. */
    statuses: {
      Uploaded: "Waiting",
      Processing: "Processing",
      Indexed: "Indexed",
      Failed: "Failed",
    } as Record<string, string | undefined>,
    /** Table labels by API type; an unknown type shows as sent. */
    types: {
      Sop: "SOP",
      License: "License",
      Log: "Log",
      Report: "Report",
      Other: "Other",
    } as Record<string, string | undefined>,
    /** The upload dialog's options, in this order. */
    typeOptions: {
      Sop: "SOP (standard operating procedure)",
      License: "License or permit",
      Log: "Log (temperatures, cleaning, deliveries)",
      Report: "Report (lab or audit)",
      Other: "Other",
    },
    /** By failure code (the queue contract's reason codes and `timed_out`). */
    failures: {
      unsupported_format: {
        short: "Unsupported format",
        long: "This file type isn't supported. Upload a PDF, DOCX, TXT or CSV file.",
      },
      no_text_layer: {
        short: "No text layer",
        long: "This PDF has no selectable text, which usually means it's a scan. Upload a PDF exported from the original document.",
      },
      too_many_pages: {
        short: "Too many pages",
        long: "This document has more than 100 pages. Split it and upload each part as its own document.",
      },
      too_large: {
        short: "Too large",
        long: "This file is larger than 10 MB. Reduce its size or split it, then upload it again.",
      },
      blob_missing: {
        short: "File missing",
        long: "The uploaded file couldn't be found. Upload it again.",
      },
      parse_error: {
        short: "Couldn't read the file",
        long: "The file may be damaged or password-protected. Check that it opens, then upload it again.",
      },
      internal: {
        short: "Internal error",
        long: "Something went wrong on our side. Upload the file again later.",
      },
      // A storage path outside the tenant: our fault, as far as the user is concerned.
      blob_path_outside_tenant: {
        short: "Internal error",
        long: "Something went wrong on our side. Upload the file again later.",
      },
      timed_out: {
        short: "Timed out",
        long: "Processing took too long and was stopped. Upload the file again.",
      },
    } as Record<string, { short: string; long: string } | undefined>,
    failureUnknown: {
      short: "Couldn't be indexed",
      long: "Something went wrong while processing this file. Upload it again.",
    },
  },
  inspections: {
    /** Pill labels by API status; an unknown status shows as sent. */
    statuses: {
      Requested: "Waiting",
      Running: "Running",
      Completed: "Completed",
      Failed: "Failed",
    } as Record<string, string | undefined>,
  },
  findings: {
    verdicts: {
      compliant: "Compliant",
      non_compliant: "Non-compliant",
      insufficient_evidence: "Insufficient evidence",
    } as Record<string, string | undefined>,
    reviews: {
      Pending: "Pending",
      Accepted: "Accepted",
      Rejected: "Rejected",
      Edited: "Edited",
    } as Record<string, string | undefined>,
    severities: { critical: "Critical", major: "Major", minor: "Minor" } as Record<
      string,
      string | undefined
    >,
  },
  /** Checklist themes in display order; an unknown theme shows as sent. */
  themes: {
    personal_hygiene: "Personal hygiene",
    temperature_control: "Temperature control",
    cross_contamination_allergens: "Cross-contamination and allergens",
    cleaning_sanitizing: "Cleaning and sanitizing",
    pest_control: "Pest control",
    water_premises: "Water and premises",
    waste: "Waste",
    documentation_training: "Documentation and training",
  } as Record<string, string | undefined>,
  upload: {
    title: "Upload document",
    description: "Add a document to {establishment}.",
    versionTitle: "Upload a new version",
    versionDescription:
      "“{title}” keeps its title and type. Version {c} stays in use until the new one is indexed.",
    versionDescriptionNoCurrent: "“{title}” keeps its title and type.",
    zone: "Choose a file or drop it here",
    zoneHint: "PDF, DOCX, TXT or CSV · up to 10 MB and 100 pages",
    zoneDragging: "Drop the file here",
    another: "Choose another file",
    chosen: "{file} chosen.",
    titleLabel: "Title",
    typeLabel: "Type",
    typePlaceholder: "Choose a type",
    cancel: "Cancel",
    submit: "Upload",
    submitting: "Uploading…",
    fileRequired: "Choose a file to upload.",
    wrongType: "“{file}” isn't a PDF, DOCX, TXT or CSV file.",
    tooLarge: "“{file}” is larger than 10 MB.",
    oneFile: "Drop one file at a time.",
    invalidContent: "“{file}” isn't a valid PDF, DOCX, TXT or CSV file.",
    titleRequired: "Enter a title.",
    typeRequired: "Choose a type.",
    limitReached:
      "This establishment has reached its document limit. Delete a document before uploading another.",
    versionLimitReached:
      "This document has reached its version limit. Upload the file as a new document.",
    inFlight:
      "Another version of this document is already being processed. Try again when it finishes.",
    network: "Couldn't upload the file. Check your connection and try again.",
  },
  establishmentTypes: {
    Restaurant: "Restaurant",
    Pharmacy: "Pharmacy",
    Clinic: "Clinic",
  } as Record<string, string | undefined>,
  jurisdictions: {
    US: {
      name: "United States",
      regulation: "FDA Food Code 2026",
      askLanguage: "Ask in English. Sources are quoted in English.",
    },
    EU: {
      name: "European Union",
      regulation: "Regulation (EC) 852/2004",
      askLanguage: "Ask in English or Portuguese. Sources are quoted in the language you ask in.",
    },
    BR: {
      name: "Brazil",
      regulation: "Anvisa RDC 216/2004",
      askLanguage: "Ask in Portuguese. Sources are quoted in Portuguese.",
    },
  } as Record<string, { name: string; regulation: string; askLanguage: string } | undefined>,
};

export function format(template: string, values: Record<string, string>): string {
  return template.replace(/\{(\w+)\}/g, (match, key: string) => values[key] ?? match);
}

/** The time today, "Nov 10" this year, "Nov 10, 2025" before. */
export function shortDate(iso: string, now: Date): string {
  const date = new Date(iso);
  const options: Intl.DateTimeFormatOptions =
    date.toDateString() === now.toDateString()
      ? { hour: "numeric", minute: "2-digit" }
      : date.getFullYear() === now.getFullYear()
        ? { month: "short", day: "numeric" }
        : { month: "short", day: "numeric", year: "numeric" };
  return new Intl.DateTimeFormat(locale, options).format(date);
}

/** "Portuguese" for "pt"; the code itself when the browser can't name it. */
export function languageName(code: string): string {
  try {
    return new Intl.DisplayNames([locale], { type: "language" }).of(code) ?? code;
  } catch {
    return code;
  }
}

const MEGABYTE = 1024 * 1024;

/** "2.4 MB" from 1 MB, "830 kB" below; 1024-based, as the upload limit is. */
export function fileSize(bytes: number): string {
  const mega = bytes >= MEGABYTE;
  return new Intl.NumberFormat(locale, {
    style: "unit",
    unit: mega ? "megabyte" : "kilobyte",
    maximumFractionDigits: 1,
  }).format(bytes / (mega ? MEGABYTE : 1024));
}
