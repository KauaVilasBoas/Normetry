import { useLocation } from "react-router";

const conversations = "/conversations";
const documents = "/documents";
const inspections = "/inspections";
const settings = "/settings";
const profiles = `${settings}/profiles`;

/** The search params a link into an inspection's findings presets the filters with. */
export const findingFilterParams = { verdict: "verdict", theme: "theme" } as const;

/** A verdict or theme code (the contract's) to preset an inspection's findings filters with. */
type FindingFilter = Partial<Record<keyof typeof findingFilterParams, string>>;

/** Where links and navigations go. The builders encode an id once. */
export const paths = {
  home: "/",
  overview: "/overview",
  ask: "/ask",
  conversation: (id: string) => `${conversations}/${encodeURIComponent(id)}`,
  documents,
  document: (id: string) => `${documents}/${encodeURIComponent(id)}`,
  inspections,
  inspection: (id: string, filter: FindingFilter = {}) => {
    const search = new URLSearchParams();
    if (filter.verdict) search.set(findingFilterParams.verdict, filter.verdict);
    if (filter.theme) search.set(findingFilterParams.theme, filter.theme);
    const query = search.toString();
    return `${inspections}/${encodeURIComponent(id)}${query && `?${query}`}`;
  },
  settings,
  profiles,
  profile: (id: string) => `${profiles}/${encodeURIComponent(id)}`,
};

/**
 * The patterns `<Route>` and the sidebar's `matchPath` match: the routes that take an id,
 * Settings' whole section (whichever tab is open), and the rest.
 */
export const routePatterns = {
  conversation: `${conversations}/:conversationId`,
  documents: `${documents}/:documentId?`,
  inspections: `${inspections}/:inspectionId?`,
  profiles: `${profiles}/:profileId?`,
  settingsSection: `${settings}/*`,
  fallback: "*",
};

/** Whether the search params preset a findings filter. */
export const hasFindingFilter = (params: URLSearchParams) =>
  Object.values(findingFilterParams).some((name) => params.has(name));

/** The state a link from another screen carries: the page it opens focuses its heading. */
export const arrivalState = { focusTitle: true } as const;

/** The location's key when a link carried `arrivalState` here, else undefined. */
export function useArrivalKey() {
  const location = useLocation();
  const state = location.state as Partial<typeof arrivalState> | null;
  return state?.focusTitle === true ? location.key : undefined;
}
