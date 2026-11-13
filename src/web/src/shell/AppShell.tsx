import { Menu, X } from "lucide-react";
import { Dialog } from "radix-ui";
import { useEffect, useRef, useState } from "react";
import { useEstablishments, useMe, type Establishment } from "../api/queries";
import { useSession } from "../auth/session";
import { copy, format } from "../copy";
import { ChatPlaceholder } from "../establishments/ChatPlaceholder";
import { EstablishmentPicker } from "../establishments/EstablishmentPicker";
import { listState, useEstablishmentSelection } from "../establishments/selection";
import { Sidebar } from "./Sidebar";

const iconButton =
  "inline-flex size-9 shrink-0 items-center justify-center rounded-control text-text-secondary hover:bg-elevated";

/** The app frame: sidebar (a drawer below 1024px), top bar and main. */
export function AppShell() {
  const { profile } = useSession();
  const me = useMe();
  const establishments = useEstablishments();
  const state = listState(me, establishments);
  const { selected, select } = useEstablishmentSelection(me.data, establishments.data);

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

  const chooseInPicker = (establishment: Establishment) => {
    select(establishment);
    setFocusKey((key) => key + 1);
  };

  const chooseInSwitcher = (establishment: Establishment) => {
    select(establishment);
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

  const sidebarProps = {
    me,
    state,
    establishments: establishments.data,
    selected,
    onSelect: chooseInSwitcher,
  };

  return (
    <div className="flex h-dvh bg-app">
      <title>{copy.titles.app}</title>
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
            <h1 className="text-title font-semibold">{copy.shell.askTheRegulation}</h1>
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
          {/* Any failure, /me included, shows the picker with its message and Try again. */}
          {selected && state === "list" ? (
            <ChatPlaceholder establishment={selected} focusKey={focusKey} />
          ) : (
            <EstablishmentPicker
              state={state}
              establishments={establishments.data}
              givenName={profile.givenName}
              onSelect={chooseInPicker}
              onRetry={retry}
              fetching={me.isFetching || establishments.isFetching}
            />
          )}
        </main>
      </div>
      <p aria-live="polite" className="sr-only">
        {announcement}
      </p>
    </div>
  );
}
