import type { UseQueryResult } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { useNavigate, type Params } from "react-router";
import { useConversations } from "../api/chat";
import { useDocument } from "../api/documents";
import { useInspection } from "../api/inspections";
import type { Establishment, Me } from "../api/queries";
import { copy, dateTime, format } from "../copy";
import type { ListState, useEstablishmentSelection } from "../establishments/selection";
import type { Screen, ScreenAccess } from "./screens";

/** The route's params: a conversation, a document or an inspection opened by URL. */
export type Routed = Readonly<Params>;

/** The selected establishment and how to select another. */
export type Selection = ReturnType<typeof useEstablishmentSelection>;

/**
 * A conversation or a document opened by URL selects its establishment when it is one of the
 * user's, even before one is selected (the chat does the same from the loaded conversation).
 * Once per conversation or document, so a later choice in the switcher wins. Anyone else's
 * shows as not found.
 */
export function useRoutedEstablishment(
  screen: Screen,
  { conversationId, documentId }: Routed,
  access: ScreenAccess,
  establishments: Establishment[] | undefined,
  { selected, select }: Selection,
) {
  // Held on the whole chat screen: enabled only once a new conversation's URL lands, it would
  // refetch the list over the row the chat has just added.
  const conversations = useConversations(access.opens.chat && screen === "chat");
  const openDocument = useDocument(documentId, access.opens.documents);
  const routedId = conversationId ?? documentId;
  const routedEstablishment =
    conversationId !== undefined
      ? conversations.data?.find((conversation) => conversation.id === conversationId)
          ?.establishmentId
      : openDocument.data?.establishmentId;
  const owner = establishments?.find((item) => item.id === routedEstablishment);
  const synced = useRef<string>(undefined);

  useEffect(() => {
    if (routedId === undefined) synced.current = undefined;
    if (!owner || synced.current === routedId) return;
    synced.current = routedId;
    if (owner.id !== selected?.id) select(owner);
  }, [routedId, owner, selected, select]);
}

/** The shell's focus requests: each new `focusKey` focuses the screen's heading. */
export function useScreenFocus() {
  const [focusKey, setFocusKey] = useState(0);
  return { focusKey, focusScreen: () => setFocusKey((key) => key + 1) };
}

/**
 * The drawer (the sidebar below 1024px) and a choice in the switcher. The screen goes to its
 * section's `root` for the establishment; without one (Settings) it stays where it is. A choice
 * in the drawer closes it and focuses the screen's heading; anywhere else focus stays on the
 * switcher and the polite region announces the choice.
 */
export function useSwitcherChoice(
  root: string | undefined,
  select: (establishment: Establishment) => void,
  focusScreen: () => void,
) {
  const navigate = useNavigate();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [announcement, setAnnouncement] = useState("");
  const focusScreenAfterDrawer = useRef(false);

  // From 1024px the sidebar is persistent, so an open drawer closes when the window widens.
  useEffect(() => {
    const desktop = window.matchMedia("(min-width: 1024px)");
    const closeOnDesktop = () => {
      if (desktop.matches) setDrawerOpen(false);
    };
    desktop.addEventListener("change", closeOnDesktop);
    return () => desktop.removeEventListener("change", closeOnDesktop);
  }, []);

  const announce = (establishment: Establishment) =>
    setAnnouncement(format(copy.switcher.selected, { name: establishment.name }));

  const choose = (establishment: Establishment) => {
    select(establishment);
    if (root === undefined) {
      setDrawerOpen(false);
      announce(establishment);
      return;
    }
    void navigate(root);
    if (drawerOpen) {
      focusScreenAfterDrawer.current = true;
      setDrawerOpen(false);
    } else {
      // Focus stays on the switcher.
      announce(establishment);
    }
  };

  // A choice inside the drawer moves focus to the screen, not back to the menu button.
  const onDrawerCloseAutoFocus = (event: Event) => {
    if (!focusScreenAfterDrawer.current) return;
    focusScreenAfterDrawer.current = false;
    event.preventDefault();
    focusScreen();
  };

  return { drawerOpen, setDrawerOpen, announcement, choose, onDrawerCloseAutoFocus };
}

/**
 * Try again on the establishments card refetches what failed. Once that loads the list, focus
 * moves to the screen's heading: the button that held it is gone. Adjusted during render, as
 * React advises for state that follows other state.
 */
export function useRetryFocus(
  me: UseQueryResult<Me>,
  establishments: UseQueryResult<Establishment[]>,
  state: ListState,
  focusScreen: () => void,
) {
  const [retried, setRetried] = useState(false);
  if (retried && state !== "error") {
    setRetried(false);
    if (state === "list") focusScreen();
  }

  return () => {
    setRetried(true);
    if (me.isError) void me.refetch();
    if (establishments.isError) void establishments.refetch();
  };
}

/** The page title: the open document's or inspection's once it has loaded, else the screen's. */
export function usePageTitle(
  screenTitle: string,
  { documentId, inspectionId }: Routed,
  access: ScreenAccess,
  selected: Establishment | undefined,
) {
  // Shared with the screens (and the document with useRoutedEstablishment): no extra request.
  const openDocument = useDocument(documentId, access.opens.documents);
  const openInspection = useInspection(
    selected?.id ?? "",
    inspectionId,
    access.opens.inspections && selected !== undefined,
  );

  if (openDocument.data) return format(copy.titles.document, { title: openDocument.data.title });
  if (openInspection.data)
    return format(copy.titles.inspection, { date: dateTime(openInspection.data.requestedAt) });
  return screenTitle;
}
