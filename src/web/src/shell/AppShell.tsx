import { Lock, Menu, X } from "lucide-react";
import { Dialog } from "radix-ui";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { Navigate, useNavigate, useParams } from "react-router";
import { ApiError } from "../api/client";
import {
  can,
  useConversations,
  useEstablishments,
  useMe,
  type Establishment,
} from "../api/queries";
import { useSession } from "../auth/session";
import { ChatScreen } from "../chat/ChatScreen";
import { copy, format } from "../copy";
import { EstablishmentPicker } from "../establishments/EstablishmentPicker";
import { listState, useEstablishmentSelection } from "../establishments/selection";
import { ProfilesScreen } from "../settings/ProfilesScreen";
import { iconButton, Message } from "../ui/primitives";
import { Sidebar } from "./Sidebar";

/**
 * The app frame: sidebar (a drawer below 1024px), top bar and main. Main shows what `/me`'s
 * permissions allow; while `/me` is pending nothing gated shows. The server checks every call.
 * Settings belongs to the organization, so it shows without an establishment.
 */
export function AppShell({ screen }: { screen: "chat" | "settings" }) {
  const { profile } = useSession();
  const me = useMe();
  const chatAllowed = can(me.data, "chat.ask");
  const establishmentsAllowed = can(me.data, "establishments.read");
  const establishments = useEstablishments(establishmentsAllowed);
  const state = listState(me, establishments);
  const { selected, select } = useEstablishmentSelection(me.data, establishments.data);
  const { conversationId } = useParams();
  const navigate = useNavigate();
  const conversations = useConversations(chatAllowed && screen === "chat");

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

  // A conversation opened by URL selects its establishment when it is one of the user's, even before
  // one is selected (the chat does the same from the loaded conversation). Once per conversation,
  // so a later choice in the switcher wins. Anyone else's shows as not found.
  const conversationEstablishment = conversations.data?.find(
    (conversation) => conversation.id === conversationId,
  )?.establishmentId;
  const owner = establishments.data?.find((item) => item.id === conversationEstablishment);
  const syncedConversation = useRef<string>(undefined);
  useEffect(() => {
    if (conversationId === undefined) syncedConversation.current = undefined;
    if (!owner || syncedConversation.current === conversationId) return;
    syncedConversation.current = conversationId;
    if (owner.id !== selected?.id) select(owner);
  }, [conversationId, owner, selected, select]);

  // Choosing an establishment opens a new conversation for it.
  const chooseInPicker = (establishment: Establishment) => {
    select(establishment);
    void navigate("/");
    setFocusKey((key) => key + 1);
  };

  const chooseInSwitcher = (establishment: Establishment) => {
    select(establishment);
    // Settings stays where it is; the chat opens a new conversation for the establishment.
    if (screen === "settings") {
      setDrawerOpen(false);
      setAnnouncement(format(copy.switcher.selected, { name: establishment.name }));
      return;
    }
    void navigate("/");
    if (drawerOpen) {
      focusRegionAfterDrawer.current = true;
      setDrawerOpen(false);
    } else {
      // Focus stays on the switcher.
      setAnnouncement(format(copy.switcher.selected, { name: establishment.name }));
    }
  };

  const retry = () => {
    if (me.isError) void me.refetch();
    if (establishments.isError) void establishments.refetch();
  };

  let content: ReactNode;
  if (me.data?.permissions.length === 0) {
    content = <AccessCard title={copy.access.noneTitle} body={copy.access.noneBody} />;
  } else if (screen === "settings") {
    if (me.error instanceof ApiError && me.error.status === 403)
      content = <AccessCard title={copy.picker.noTenantTitle} body={copy.picker.noTenantBody} />;
    else if (me.data && !can(me.data, "profiles.read"))
      content = (
        <AccessCard
          title={format(copy.access.refusedTitle, { screen: copy.shell.settings })}
          body={copy.access.refusedBody}
        />
      );
    else content = <ProfilesScreen me={me} />;
  } else if (me.data && !chatAllowed) {
    // Home goes to the first screen the user may open.
    content =
      conversationId === undefined ? (
        can(me.data, "profiles.read") ? (
          <Navigate to="/settings/profiles" replace />
        ) : (
          <AccessCard title={copy.access.noneTitle} body={copy.access.noneBody} />
        )
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
    // Any failure, /me included, shows the picker with its message and Try again.
    content = (
      <EstablishmentPicker
        state={state}
        establishments={establishments.data}
        givenName={profile.givenName}
        onSelect={chooseInPicker}
        onRetry={retry}
        fetching={me.isFetching || establishments.isFetching}
      />
    );
  }

  const sidebarProps = {
    me,
    state,
    establishments: establishments.data,
    selected,
    onSelect: chooseInSwitcher,
  };

  return (
    <div className="flex h-dvh bg-app">
      <title>{screen === "settings" ? copy.titles.profiles : copy.titles.app}</title>
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
            <h1 className="text-title font-semibold">
              {screen === "settings" ? copy.shell.settings : copy.shell.askTheRegulation}
            </h1>
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

/** A refused or no-access state in main: the picker's card with a lock. */
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
