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
    inspections: "Simulated Inspection · Normetry",
    inspection: "Inspection of {date} · Simulated Inspection · Normetry",
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
    inspections: "Simulated Inspection",
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
      "Documents, Simulated Inspection and Ask the Regulation work per establishment. Ask an administrator in your organization for access.",
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
    oneInspection: "1 inspection",
    count: "{count} inspections",
    run: "Run inspection",
    starting: "Starting…",
    loading: "Loading inspections…",
    tableCaption: "Inspections of {establishment}",
    requested: "Requested",
    status: "Status",
    results: "Results",
    resultCounts:
      "{nonCompliant} non-compliant · {insufficient} insufficient evidence · {compliant} compliant",
    checked: "{evaluated} of {total} checked",
    noResults: "No results",
    emptyTitle: "No inspections yet",
    emptyBody:
      "Run an inspection to check {establishment}'s documents against {regulation}. It takes a few minutes.",
    emptyBodyReadOnly: "Inspections of {establishment} appear here.",
    errorTitle: "Couldn't load inspections",
    errorBody: "Check your connection and try again.",
    tryAgain: "Try again",
    reconnecting: "Live updates paused. Reconnecting…",
    stopped: "Live updates stopped. Progress may be out of date.",
    reconnect: "Reconnect",
    /** Pill labels by API status; an unknown status shows as sent. */
    statuses: {
      Requested: "Waiting",
      Running: "Running",
      Completed: "Completed",
      Failed: "Failed",
    } as Record<string, string | undefined>,
    runningTitle: "An inspection is running.",
    runningBody: "You can run another one when it finishes.",
    viewProgress: "View progress",
    noDocumentsTitle: "{establishment} has no indexed documents yet.",
    noDocumentsBody:
      "An inspection checks the establishment's own documents. Upload SOPs, logs or reports, and run it once at least one is indexed.",
    goToDocuments: "Go to Documents",
    quotaTitle: "Your organization has run all of today's inspections.",
    quotaBody: "You can run another inspection {when}.",
    capacityTitle: "Your organization already has inspections running.",
    capacityBody: "You can run another one when one of them finishes.",
    rateLimitedTitle: "Too many requests right now.",
    rateLimitedBody: "Try again {when}.",
    startErrorTitle: "Couldn't start the inspection.",
    startErrorBody: "Check your connection and try again.",
    denied: "You no longer have permission to do this.",
    back: "Simulated Inspection",
    title: "Inspection of {date}",
    meta: "{regulation} · {establishment}",
    loadingInspection: "Loading inspection…",
    loadErrorTitle: "Couldn't load this inspection",
    notFoundTitle: "This inspection isn't available",
    notFoundBody: "It may belong to another establishment, or it no longer exists.",
    backToInspections: "Back to inspections",
    stepsLabel: "Inspection steps",
    steps: ["Analysis", "Findings", "Action plan"],
    stepDone: "Done:",
    waitingTitle: "Waiting to start…",
    waitingBody: "The inspection is queued and starts in a moment.",
    progressTitle: "Inspection in progress…",
    progressBody: "Checking {establishment}'s documents against {regulation}.",
    preparingTitle: "Preparing findings…",
    preparingBody:
      "Every requirement is checked. Normetry is writing the findings and the action plan.",
    progressLabel: "Requirements checked",
    progress: "{evaluated} of {total} requirements checked",
    leaveNote: "You can leave this page. The inspection keeps running.",
    themesLabel: "Themes",
    themeCount: "{evaluated} of {total}",
    themeDone: "Done:",
    themeActive: "In progress:",
    themePending: "Not started:",
    failedTitle: "This inspection stopped before it finished.",
    /** By failure code (the queue contract's reason codes and `timed_out`). */
    failures: {
      budget_exceeded: {
        short: "Processing limit reached",
        long: "It needed more AI processing than one inspection is allowed, so it stopped. No findings were kept. Run it again, and if it stops again, contact your administrator.",
      },
      no_documents: {
        short: "No indexed documents",
        long: "No indexed document of this establishment was found when it started. Upload documents, wait until they're indexed, then run it again.",
      },
      error: {
        short: "Internal error",
        long: "Something went wrong on our side. No findings were kept. Run it again later.",
      },
      timed_out: {
        short: "Timed out",
        long: "It took too long and was stopped. No findings were kept. Run it again.",
      },
    } as Record<string, { short: string; long: string } | undefined>,
    failureUnknown: {
      short: "Couldn't finish",
      long: "It stopped before it finished. No findings were kept. Run it again.",
    },
    started: "Inspection started.",
    milestone: "{evaluated} of {total} requirements checked.",
    completed: "Inspection complete: {findings}.",
    failed: "The inspection stopped: {reason}.",
  },
  findings: {
    title: "Findings",
    one: "1 finding",
    count: "{count} findings",
    caption: "{findings} · {review}",
    pending: "{count} pending review",
    allReviewed: "All reviewed",
    tableCaption: "Findings of the inspection of {date}",
    requirement: "Requirement",
    theme: "Theme",
    severity: "Severity",
    verdict: "Verdict",
    review: "Review",
    meta: "{severity} · {theme}",
    verdictFilter: "Filter by verdict",
    allVerdicts: "All verdicts",
    themeFilter: "Filter by theme",
    allThemes: "All themes",
    option: "{label} ({count})",
    noMatchTitle: "No findings match these filters",
    noMatchBody: "Change or clear the filters to see more findings.",
    clearFilters: "Clear filters",
    noneTitle: "No findings were kept",
    noneBody:
      "Normetry keeps a finding only when it can quote the regulation, and it couldn't for any requirement in this inspection.",
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
    confidences: { high: "High", medium: "Medium", low: "Low" } as Record<
      string,
      string | undefined
    >,
    position: "Finding {n} of {total}",
    previous: "Previous finding",
    next: "Next finding",
    close: "Close finding",
    confidence: "Confidence",
    rationale: "Rationale",
    regulation: "Regulation",
    evidence: "Evidence",
    noEvidence: "No passage in this establishment's documents shows this requirement.",
    regulationMeta: "{source} · {section}",
    sourceMeta: "Version {version} · {language}",
    untitledDocument: "Untitled document",
    openDocument: "Open document",
    wholePassage: "Show the whole passage",
    storedCopy:
      "Quotes are copies kept with the finding. The regulation or a document may have changed since.",
    action: "Action",
    due: "Due {date}",
    noAction: "No action needed.",
    reviewHeading: "Review",
    notReviewed: "No one has reviewed this finding yet.",
    acceptedBy: "Accepted by {name} on {date}.",
    rejectedBy: "Rejected by {name} on {date}.",
    editedBy: "Edited by {name} on {date}.",
    reason: "Reason",
    unknownReviewer: "Unknown user",
    original: "Show the original values",
    dueDate: "Due date",
    accept: "Accept",
    accepting: "Accepting…",
    reject: "Reject",
    edit: "Edit",
    reviewError: "Couldn't save the review. Try again.",
    gone: "This finding isn't available anymore.",
    accepted: "Finding accepted.",
    rejected: "Finding rejected.",
    edited: "Finding updated.",
    moved: "{title}. Finding {n} of {total}.",
    rejectTitle: "Reject finding",
    reasonHint: "Say why this finding is wrong. Up to 1,000 characters.",
    reasonRequired: "Enter a reason.",
    rejectSubmit: "Reject finding",
    rejecting: "Rejecting…",
    editTitle: "Edit finding",
    editNote: "The original values stay available on the finding.",
    chooseVerdict: "Choose a verdict",
    chooseSeverity: "Choose a severity",
    actionHint: "What the establishment should do, in one or two sentences.",
    rationaleRequired: "Enter a rationale.",
    actionRequired: "Describe the action.",
    dueRequired: "Choose a due date.",
    save: "Save changes",
    saving: "Saving…",
    cancel: "Cancel",
  },
  actions: {
    title: "Action plan",
    caption: "{open} open · {done} done",
    meta: "{finding} · Due {date}",
    dueIn: "Due {when}",
    overdue: "Overdue",
    done: "Done:",
    open: "Open:",
    error: "Couldn't update the action. Try again.",
    emptyTitle: "No actions to take",
    emptyBody:
      "Actions appear for non-compliant findings and for findings without enough evidence.",
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

/** "Nov 17, 2026, 10:42 AM": a date and time, so two on the same day stay distinct. */
export function dateTime(iso: string): string {
  return new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" }).format(
    new Date(iso),
  );
}

/**
 * A date with no time ("2026-11-23") at local midnight. `new Date("2026-11-23")` would be UTC
 * midnight, the day before west of UTC.
 */
export function localDate(date: string): Date {
  const [year, month, day] = date.split("-").map(Number);
  return new Date(year, month - 1, day);
}

/** "Nov 23" this year, "Nov 23, 2027" in another, for a date with no time. */
export function calendarDate(date: string, now = new Date()): string {
  const day = localDate(date);
  return new Intl.DateTimeFormat(locale, {
    month: "short",
    day: "numeric",
    ...(day.getFullYear() === now.getFullYear() ? {} : { year: "numeric" }),
  }).format(day);
}

/** "in 7 days", "tomorrow", "today": `days` from today. */
export const relativeDays = (days: number) =>
  new Intl.RelativeTimeFormat(locale, { numeric: "auto" }).format(days, "day");

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
