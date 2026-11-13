// Every interface string, in English for now (PT comes with the language switch). Whole sentences
// only, with {placeholders}; never build a sentence by concatenation.
export const copy = {
  tagline: "Understand Regulations. Stay Ahead.",
  titles: {
    signingIn: "Signing in · Normetry",
    signedOut: "Signed out · Normetry",
    app: "Ask the Regulation · Normetry",
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
    navigation: "Navigation",
    openNavigation: "Open navigation",
    closeNavigation: "Close navigation",
    organization: "Organization:",
    accountMenu: "Account menu, {name}",
    signOut: "Sign out",
  },
  switcher: {
    placeholder: "Choose establishment",
    none: "No establishment",
    name: "Establishment",
    selected: "{name} selected",
    loading: "Loading establishments…",
  },
  picker: {
    greeting: "Welcome back, {name}",
    greetingNoName: "Welcome back",
    subtitle: "Choose an establishment to ask about the regulation that applies to it.",
    title: "Establishments",
    loading: "Loading establishments…",
    optionLabel: "{name}, {type}, {jurisdiction}",
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
  chat: {
    placeholder: "Questions about this establishment's regulation will appear here.",
  },
  establishmentTypes: {
    Restaurant: "Restaurant",
    Pharmacy: "Pharmacy",
    Clinic: "Clinic",
  } as Record<string, string | undefined>,
  jurisdictions: {
    US: { name: "United States", regulation: "FDA Food Code 2026" },
    EU: { name: "European Union", regulation: "Regulation (EC) 852/2004" },
    BR: { name: "Brazil", regulation: "Anvisa RDC 216/2004" },
  } as Record<string, { name: string; regulation: string } | undefined>,
};

export function format(template: string, values: Record<string, string>): string {
  return template.replace(/\{(\w+)\}/g, (match, key: string) => values[key] ?? match);
}
