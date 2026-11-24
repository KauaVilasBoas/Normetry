const conversations = "/conversations";
const documents = "/documents";
const inspections = "/inspections";
const settings = "/settings";
const profiles = `${settings}/profiles`;

/** Where links and navigations go. The builders encode an id once. */
export const paths = {
  home: "/",
  overview: "/overview",
  ask: "/ask",
  conversation: (id: string) => `${conversations}/${encodeURIComponent(id)}`,
  documents,
  document: (id: string) => `${documents}/${encodeURIComponent(id)}`,
  inspections,
  inspection: (id: string) => `${inspections}/${encodeURIComponent(id)}`,
  settings,
  profiles,
  profile: (id: string) => `${profiles}/${encodeURIComponent(id)}`,
};

/** The patterns `<Route>` and `useMatch` match: the routes that take an id, and the rest. */
export const routePatterns = {
  conversation: `${conversations}/:conversationId`,
  documents: `${documents}/:documentId?`,
  inspections: `${inspections}/:inspectionId?`,
  profiles: `${profiles}/:profileId?`,
  fallback: "*",
};
