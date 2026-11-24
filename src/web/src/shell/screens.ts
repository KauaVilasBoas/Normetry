import {
  ClipboardCheck,
  FileText,
  House,
  MessagesSquare,
  Settings,
  type LucideIcon,
} from "lucide-react";
import { permissions } from "../api/contract";
import { can, type Me } from "../api/queries";
import { copy } from "../copy";
import { paths, routePatterns } from "../routes";

/** The shell's screens, one per group of routes in `App.tsx`. */
export type Screen = "overview" | "chat" | "documents" | "inspections" | "settings";

interface NavItem {
  path: string;
  /** The route patterns where the item is the current page. */
  match: string[];
  label: string;
  Icon: LucideIcon;
  screen: Screen;
}

/** The sidebar's items, in order. Each section's routes, list and detail alike, are its item's. */
const navItems: NavItem[] = [
  {
    path: paths.overview,
    match: [paths.overview],
    label: copy.shell.overview,
    Icon: House,
    screen: "overview",
  },
  {
    path: paths.ask,
    // A new conversation and a saved one are both Ask the Regulation.
    match: [paths.ask, routePatterns.conversation],
    label: copy.shell.askTheRegulation,
    Icon: MessagesSquare,
    screen: "chat",
  },
  {
    path: paths.documents,
    match: [routePatterns.documents],
    label: copy.shell.documents,
    Icon: FileText,
    screen: "documents",
  },
  {
    path: paths.inspections,
    match: [routePatterns.inspections],
    label: copy.shell.inspections,
    Icon: ClipboardCheck,
    screen: "inspections",
  },
  {
    path: paths.profiles,
    match: [routePatterns.settingsSection],
    label: copy.shell.settings,
    Icon: Settings,
    screen: "settings",
  },
];

/**
 * What `/me`'s permissions open (nothing while it is unknown): each screen, the sidebar's items
 * for them, home and the establishments list. The server still checks every call.
 */
export function screenAccess(me: Me | undefined) {
  const opens: Record<Screen, boolean> = {
    overview: can(me, permissions.inspections.read) || can(me, permissions.documents.read),
    chat: can(me, permissions.chat.ask),
    documents: can(me, permissions.documents.read),
    inspections: can(me, permissions.inspections.read),
    settings: can(me, permissions.profiles.read),
  };
  const nav = navItems.filter((item) => opens[item.screen]);
  return {
    opens,
    nav,
    // The first screen the user may open, in nav order: Overview (either read permission opens
    // it, so Documents and Simulated Inspection are never home), the chat, then Settings. With
    // none, main shows the no-access card instead.
    home: nav.at(0)?.path ?? paths.home,
    establishments: can(me, permissions.establishments.read),
  };
}
