import type { UseQueryResult } from "@tanstack/react-query";
import { Lock, Menu, X } from "lucide-react";
import { Dialog } from "radix-ui";
import type { ReactNode } from "react";
import { Navigate, useParams } from "react-router";
import { useEstablishments, useMe, type Establishment, type Me } from "../api/queries";
import { ChatScreen } from "../chat/ChatScreen";
import { copy, format } from "../copy";
import { DocumentsScreen } from "../documents/DocumentsScreen";
import { EstablishmentPicker } from "../establishments/EstablishmentPicker";
import { listState, useEstablishmentSelection, type ListState } from "../establishments/selection";
import { InspectionsScreen } from "../inspections/InspectionsScreen";
import { OverviewScreen } from "../overview/OverviewScreen";
import { paths } from "../routes";
import { ProfilesScreen } from "../settings/ProfilesScreen";
import { iconButton, Message } from "../ui/primitives";
import {
  ACCESS_TITLE_ID,
  useLostScreenFocus,
  usePageTitle,
  useRetryFocus,
  useRoutedEstablishment,
  useScreenFocus,
  useSwitcherChoice,
  type Routed,
  type Selection,
} from "./frame";
import { screenAccess, type Screen, type ScreenAccess } from "./screens";
import { Sidebar } from "./Sidebar";

/** What a screen of the selected establishment is given. */
interface SectionView {
  establishment: Establishment;
  me: Me | undefined;
  routed: Routed;
  focusKey: number;
  onEstablishment: (establishment: Establishment) => void;
}

interface Frame {
  /** The top bar's heading, and the name a card refusing the screen gives it. */
  heading: string;
  /** The page title, unless an open document or inspection names it. */
  title: string;
  /** Refused, the screen goes home instead of showing a card that names it. */
  refusedGoesHome?: (routed: Routed) => boolean;
  /**
   * A section of the selected establishment: where a choice in the switcher leads, and its
   * screen. Settings belongs to the organization instead: a choice leaves it where it is, and it
   * shows without an establishment.
   */
  section?: { root: string; view: (view: SectionView) => ReactNode };
}

/** Each screen's frame, and what a user it refuses sees. */
const frames: Record<Screen, Frame> = {
  overview: {
    heading: copy.shell.overview,
    title: copy.titles.overview,
    refusedGoesHome: () => true,
    section: {
      root: paths.overview,
      view: ({ establishment, me, focusKey }) => (
        <OverviewScreen establishment={establishment} me={me} focusKey={focusKey} />
      ),
    },
  },
  chat: {
    heading: copy.shell.askTheRegulation,
    title: copy.titles.app,
    // A new conversation goes home; a saved one opened by URL is refused by name.
    refusedGoesHome: ({ conversationId }) => conversationId === undefined,
    section: {
      root: paths.ask,
      view: ({ establishment, routed, focusKey, onEstablishment }) => (
        <ChatScreen
          establishment={establishment}
          conversationId={routed.conversationId}
          focusKey={focusKey}
          onEstablishment={onEstablishment}
        />
      ),
    },
  },
  documents: {
    heading: copy.shell.documents,
    title: copy.titles.documents,
    section: {
      root: paths.documents,
      view: ({ establishment, routed, me, focusKey }) => (
        <DocumentsScreen
          establishment={establishment}
          documentId={routed.documentId}
          me={me}
          focusKey={focusKey}
        />
      ),
    },
  },
  inspections: {
    heading: copy.shell.inspections,
    title: copy.titles.inspections,
    section: {
      root: paths.inspections,
      view: ({ establishment, routed, me, focusKey }) => (
        <InspectionsScreen
          establishment={establishment}
          inspectionId={routed.inspectionId}
          me={me}
          focusKey={focusKey}
        />
      ),
    },
  },
  settings: { heading: copy.shell.settings, title: copy.titles.profiles },
};

/**
 * The app frame: sidebar (a drawer below 1024px), top bar and main. Main shows what `/me`'s
 * permissions allow; while `/me` is pending nothing gated shows. The server checks every call.
 * Settings belongs to the organization, so it shows without an establishment.
 */
export function AppShell({ screen }: { screen: Screen }) {
  const frame = frames[screen];
  const me = useMe();
  const access = screenAccess(me.data);
  const establishments = useEstablishments(access.establishments);
  const state = listState(me, establishments);
  const selection = useEstablishmentSelection(me.data, establishments.data);
  const routed = useParams();
  useRoutedEstablishment(screen, routed, access, establishments.data, selection);
  const { focusKey, focusScreen } = useScreenFocus();
  const switcher = useSwitcherChoice(frame.section?.root, selection.select, focusScreen);
  const retry = useRetryFocus(me, establishments, state, focusScreen);
  const title = usePageTitle(frame.title, routed, access, selection.selected);
  // What the screen needs, and a tenant (a 403 from /me keeps the old data).
  useLostScreenFocus(
    access.opens[screen] && (access.establishments || !frame.section) && state !== "forbidden",
    focusScreen,
  );

  const sidebarProps = {
    me,
    state,
    establishments: establishments.data,
    selected: selection.selected,
    onSelect: switcher.choose,
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
        <Dialog.Root open={switcher.drawerOpen} onOpenChange={switcher.setDrawerOpen}>
          <header className="flex h-14 shrink-0 items-center gap-2 px-4 sm:px-5">
            <Dialog.Trigger
              aria-label={copy.shell.openNavigation}
              className={`${iconButton} lg:hidden`}
            >
              <Menu aria-hidden="true" className="size-5" />
            </Dialog.Trigger>
            <h1 className="text-title font-semibold">{frame.heading}</h1>
          </header>
          <Dialog.Portal>
            <Dialog.Overlay className="fixed inset-0 z-40 bg-scrim" />
            <Dialog.Content
              aria-describedby={undefined}
              onCloseAutoFocus={switcher.onDrawerCloseAutoFocus}
              className="fixed inset-y-0 left-0 z-40 flex w-55 flex-col overflow-y-auto bg-sidebar px-4 py-5 motion-safe:animate-drawer-in"
            >
              <Dialog.Title className="sr-only">{copy.shell.navigation}</Dialog.Title>
              <Sidebar
                {...sidebarProps}
                onNavigate={() => switcher.setDrawerOpen(false)}
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
          {mainContent({
            screen,
            routed,
            me,
            access,
            establishments,
            state,
            selection,
            focusKey,
            retry,
          })}
        </main>
      </div>
      <p aria-live="polite" className="sr-only">
        {switcher.announcement}
      </p>
    </div>
  );
}

interface Main {
  screen: Screen;
  routed: Routed;
  me: UseQueryResult<Me>;
  access: ScreenAccess;
  establishments: UseQueryResult<Establishment[]>;
  state: ListState;
  selection: Selection;
  focusKey: number;
  retry: () => void;
}

/**
 * What main shows, the first that applies: no screen at all, no tenant, the screen refused,
 * Settings, no access to establishments, the screen, else the establishments card.
 */
function mainContent({
  screen,
  routed,
  me,
  access,
  establishments,
  state,
  selection,
  focusKey,
  retry,
}: Main) {
  const { heading, refusedGoesHome, section } = frames[screen];
  // No profile at all, or profiles whose permissions open no screen: the same on every route.
  if (me.data && access.nav.length === 0)
    return me.data.profiles.length === 0 ? (
      <AccessCard title={copy.access.noneTitle} body={copy.access.noneBody} />
    ) : (
      <AccessCard title={copy.access.noScreenTitle} body={copy.access.noScreenBody} />
    );
  // A token without a valid tenant: the same on every route.
  if (state === "forbidden")
    return <AccessCard title={copy.picker.noTenantTitle} body={copy.picker.noTenantBody} />;
  if (me.data && !access.opens[screen])
    return refusedGoesHome?.(routed) ? (
      <Navigate to={access.home} replace />
    ) : (
      <AccessCard
        title={format(copy.access.refusedTitle, { screen: heading })}
        body={copy.access.refusedBody}
      />
    );
  if (!section) return <ProfilesScreen me={me} focusKey={focusKey} />;
  if (me.data && !access.establishments)
    return (
      <AccessCard title={copy.access.establishmentsTitle} body={copy.access.establishmentsBody} />
    );
  if (selection.selected && state === "list")
    return section.view({
      establishment: selection.selected,
      me: me.data,
      routed,
      focusKey,
      onEstablishment: selection.select,
    });
  // Loading, no establishment or any failure (/me included): the card with its message.
  return (
    <EstablishmentPicker
      state={state}
      onRetry={retry}
      fetching={me.isFetching || establishments.isFetching}
    />
  );
}

/** A refused or no-access state in main: the establishments card's shape, with a lock. */
function AccessCard({ title, body }: { title: string; body: string }) {
  return (
    <div className="w-full max-w-160 rounded-card border border-border bg-card p-5 motion-safe:animate-fade-in">
      <Message
        heading="h2"
        headingId={ACCESS_TITLE_ID}
        className="items-start"
        icon={<Lock aria-hidden="true" className="size-5 text-neutral" />}
        title={title}
        body={body}
      />
    </div>
  );
}
