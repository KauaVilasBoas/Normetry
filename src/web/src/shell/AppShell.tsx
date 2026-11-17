import { Lock, Menu, X } from "lucide-react";
import { Dialog } from "radix-ui";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { Navigate, useNavigate, useParams } from "react-router";
import {
  can,
  useConversations,
  useDocument,
  useEstablishments,
  useInspection,
  useMe,
  type Establishment,
} from "../api/queries";
import { ChatScreen } from "../chat/ChatScreen";
import { copy, dateTime, format } from "../copy";
import { DocumentsScreen } from "../documents/DocumentsScreen";
import { EstablishmentPicker } from "../establishments/EstablishmentPicker";
import { listState, useEstablishmentSelection } from "../establishments/selection";
import { InspectionsScreen } from "../inspections/InspectionsScreen";
import { ProfilesScreen } from "../settings/ProfilesScreen";
import { iconButton, Message } from "../ui/primitives";
import { Sidebar } from "./Sidebar";

type Screen = "chat" | "documents" | "inspections" | "settings";

const headings: Record<Screen, string> = {
  chat: copy.shell.askTheRegulation,
  documents: copy.shell.documents,
  inspections: copy.shell.inspections,
  settings: copy.shell.settings,
};

/** Where an establishment chosen in the switcher leads: the section's root. */
const sectionRoots = { chat: "/", documents: "/documents", inspections: "/inspections" };

/**
 * The app frame: sidebar (a drawer below 1024px), top bar and main. Main shows what `/me`'s
 * permissions allow; while `/me` is pending nothing gated shows. The server checks every call.
 * Settings belongs to the organization, so it shows without an establishment.
 */
export function AppShell({ screen }: { screen: Screen }) {
  const me = useMe();
  const chatAllowed = can(me.data, "chat.ask");
  const documentsAllowed = can(me.data, "documents.read");
  const inspectionsAllowed = can(me.data, "inspections.read");
  const establishmentsAllowed = can(me.data, "establishments.read");
  const establishments = useEstablishments(establishmentsAllowed);
  const state = listState(me, establishments);
  const { selected, select } = useEstablishmentSelection(me.data, establishments.data);
  const { conversationId, documentId, inspectionId } = useParams();
  const navigate = useNavigate();
  const conversations = useConversations(chatAllowed && screen === "chat");
  const openDocument = useDocument(documentId, documentsAllowed && screen === "documents");
  // For the page title; the screen shares the query.
  const openInspection = useInspection(
    selected?.id ?? "",
    inspectionId,
    inspectionsAllowed && screen === "inspections" && selected !== undefined,
  );

  const [drawerOpen, setDrawerOpen] = useState(false);
  const [announcement, setAnnouncement] = useState("");
  const [focusKey, setFocusKey] = useState(0);
  const focusRegionAfterDrawer = useRef(false);

  // From 1024px the sidebar is persistent, so an open drawer closes when the window widens.
  useEffect(() => {
    const desktop = window.matchMedia("(min-width: 1024px)");
    const closeOnDesktop = () => {
      if (desktop.matches) setDrawerOpen(false);
    };
    desktop.addEventListener("change", closeOnDesktop);
    return () => desktop.removeEventListener("change", closeOnDesktop);
  }, []);

  // A conversation or a document opened by URL selects its establishment when it is one of the
  // user's, even before one is selected (the chat does the same from the loaded conversation).
  // Once per conversation or document, so a later choice in the switcher wins. Anyone else's
  // shows as not found.
  const routedId = conversationId ?? documentId;
  const routedEstablishment =
    conversationId !== undefined
      ? conversations.data?.find((conversation) => conversation.id === conversationId)
          ?.establishmentId
      : openDocument.data?.establishmentId;
  const owner = establishments.data?.find((item) => item.id === routedEstablishment);
  const synced = useRef<string>(undefined);
  useEffect(() => {
    if (routedId === undefined) synced.current = undefined;
    if (!owner || synced.current === routedId) return;
    synced.current = routedId;
    if (owner.id !== selected?.id) select(owner);
  }, [routedId, owner, selected, select]);

  const chooseInSwitcher = (establishment: Establishment) => {
    select(establishment);
    // Settings stays where it is; the other screens go to their root for the establishment.
    if (screen === "settings") {
      setDrawerOpen(false);
      setAnnouncement(format(copy.switcher.selected, { name: establishment.name }));
      return;
    }
    void navigate(sectionRoots[screen]);
    if (drawerOpen) {
      focusRegionAfterDrawer.current = true;
      setDrawerOpen(false);
    } else {
      // Focus stays on the switcher.
      setAnnouncement(format(copy.switcher.selected, { name: establishment.name }));
    }
  };

  // A Try again that loads the list moves focus to the screen's heading: its button is gone.
  // Adjusted during render, as React advises for state that follows other state.
  const [retried, setRetried] = useState(false);
  if (retried && state !== "error") {
    setRetried(false);
    if (state === "list") setFocusKey((key) => key + 1);
  }
  const retry = () => {
    setRetried(true);
    if (me.isError) void me.refetch();
    if (establishments.isError) void establishments.refetch();
  };

  // The screens the user may open. Home goes to the first: the chat, Documents, Simulated
  // Inspection, then Settings.
  const canOpenAScreen =
    chatAllowed || documentsAllowed || inspectionsAllowed || can(me.data, "profiles.read");
  const home = documentsAllowed
    ? "/documents"
    : inspectionsAllowed
      ? "/inspections"
      : "/settings/profiles";

  let content: ReactNode;
  if (me.data && !canOpenAScreen) {
    // No profile at all, or profiles whose permissions open no screen: the same on every route.
    content =
      me.data.profiles.length === 0 ? (
        <AccessCard title={copy.access.noneTitle} body={copy.access.noneBody} />
      ) : (
        <AccessCard title={copy.access.noScreenTitle} body={copy.access.noScreenBody} />
      );
  } else if (state === "forbidden") {
    // A token without a valid tenant: the same on every route.
    content = <AccessCard title={copy.picker.noTenantTitle} body={copy.picker.noTenantBody} />;
  } else if (screen === "settings") {
    if (me.data && !can(me.data, "profiles.read"))
      content = (
        <AccessCard
          title={format(copy.access.refusedTitle, { screen: copy.shell.settings })}
          body={copy.access.refusedBody}
        />
      );
    else content = <ProfilesScreen me={me} />;
  } else if (screen === "documents" && me.data && !documentsAllowed) {
    content = (
      <AccessCard
        title={format(copy.access.refusedTitle, { screen: copy.shell.documents })}
        body={copy.access.refusedBody}
      />
    );
  } else if (screen === "inspections" && me.data && !inspectionsAllowed) {
    content = (
      <AccessCard
        title={format(copy.access.refusedTitle, { screen: copy.shell.inspections })}
        body={copy.access.refusedBody}
      />
    );
  } else if (screen === "chat" && me.data && !chatAllowed) {
    content =
      conversationId === undefined ? (
        <Navigate to={home} replace />
      ) : (
        <AccessCard
          title={format(copy.access.refusedTitle, { screen: copy.shell.askTheRegulation })}
          body={copy.access.refusedBody}
        />
      );
  } else if (me.data && !establishmentsAllowed) {
    content = (
      <AccessCard title={copy.access.establishmentsTitle} body={copy.access.establishmentsBody} />
    );
  } else if (selected && state === "list" && screen === "documents") {
    content = (
      <DocumentsScreen
        establishment={selected}
        documentId={documentId}
        me={me.data}
        focusKey={focusKey}
      />
    );
  } else if (selected && state === "list" && screen === "inspections") {
    content = (
      <InspectionsScreen
        establishment={selected}
        inspectionId={inspectionId}
        me={me.data}
        focusKey={focusKey}
      />
    );
  } else if (selected && state === "list") {
    content = (
      <ChatScreen
        establishment={selected}
        conversationId={conversationId}
        focusKey={focusKey}
        onEstablishment={select}
      />
    );
  } else {
    // Loading, no establishment or any failure (/me included): the card with its message.
    content = (
      <EstablishmentPicker
        state={state}
        onRetry={retry}
        fetching={me.isFetching || establishments.isFetching}
      />
    );
  }

  const title = {
    settings: copy.titles.profiles,
    chat: copy.titles.app,
    documents:
      openDocument.data && documentId !== undefined
        ? format(copy.titles.document, { title: openDocument.data.title })
        : copy.titles.documents,
    inspections:
      openInspection.data && inspectionId !== undefined
        ? format(copy.titles.inspection, { date: dateTime(openInspection.data.requestedAt) })
        : copy.titles.inspections,
  }[screen];

  const sidebarProps = {
    me,
    state,
    establishments: establishments.data,
    selected,
    onSelect: chooseInSwitcher,
  };

  return (
    <div className="flex h-dvh bg-app">
      <title>{title}</title>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:top-4 focus:left-4 focus:z-50 focus:rounded-control focus:bg-elevated focus:px-4 focus:py-2"
      >
        {copy.shell.skipLink}
      </a>
      <aside className="hidden w-55 shrink-0 flex-col overflow-y-auto bg-sidebar px-4 py-5 lg:flex">
        <Sidebar {...sidebarProps} />
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <Dialog.Root open={drawerOpen} onOpenChange={setDrawerOpen}>
          <header className="flex h-14 shrink-0 items-center gap-2 px-4 sm:px-5">
            <Dialog.Trigger
              aria-label={copy.shell.openNavigation}
              className={`${iconButton} lg:hidden`}
            >
              <Menu aria-hidden="true" className="size-5" />
            </Dialog.Trigger>
            <h1 className="text-title font-semibold">{headings[screen]}</h1>
          </header>
          <Dialog.Portal>
            <Dialog.Overlay className="fixed inset-0 z-40 bg-scrim" />
            <Dialog.Content
              aria-describedby={undefined}
              onCloseAutoFocus={(event) => {
                // A choice inside the drawer moves focus to the region, not back to the menu button.
                if (!focusRegionAfterDrawer.current) return;
                focusRegionAfterDrawer.current = false;
                event.preventDefault();
                setFocusKey((key) => key + 1);
              }}
              className="fixed inset-y-0 left-0 z-40 flex w-55 flex-col overflow-y-auto bg-sidebar px-4 py-5 motion-safe:animate-drawer-in"
            >
              <Dialog.Title className="sr-only">{copy.shell.navigation}</Dialog.Title>
              <Sidebar
                {...sidebarProps}
                onNavigate={() => setDrawerOpen(false)}
                closeButton={
                  <Dialog.Close aria-label={copy.shell.closeNavigation} className={iconButton}>
                    <X aria-hidden="true" className="size-5" />
                  </Dialog.Close>
                }
              />
            </Dialog.Content>
          </Dialog.Portal>
        </Dialog.Root>

        <main
          id="main"
          tabIndex={-1}
          className="flex min-h-0 flex-1 flex-col overflow-y-auto px-4 pb-4 sm:px-5 sm:pb-5"
        >
          {content}
        </main>
      </div>
      <p aria-live="polite" className="sr-only">
        {announcement}
      </p>
    </div>
  );
}

/** A refused or no-access state in main: the establishments card's shape, with a lock. */
function AccessCard({ title, body }: { title: string; body: string }) {
  return (
    <div className="w-full max-w-160 rounded-card border border-border bg-card p-5 motion-safe:animate-fade-in">
      <Message
        heading="h2"
        className="items-start"
        icon={<Lock aria-hidden="true" className="size-5 text-neutral" />}
        title={title}
        body={body}
      />
    </div>
  );
}
