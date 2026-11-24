import type { UseQueryResult } from "@tanstack/react-query";
import {
  Building2,
  ChevronDown,
  ClipboardCheck,
  FileText,
  House,
  LogOut,
  MessagesSquare,
  Settings,
} from "lucide-react";
import { DropdownMenu } from "radix-ui";
import type { ReactNode } from "react";
import { Link, useMatch } from "react-router";
import { permissions } from "../api/contract";
import { can, type Establishment, type Me, type ProfileSummary } from "../api/queries";
import { useSession } from "../auth/session";
import { copy, format } from "../copy";
import { forgetEstablishments, type ListState } from "../establishments/selection";
import { paths, routePatterns } from "../routes";
import { sortProfiles } from "../settings/profiles";
import { Logo } from "../ui/Logo";
import { Avatar, Skeleton } from "../ui/primitives";
import { EstablishmentSwitcher } from "./EstablishmentSwitcher";

interface SidebarProps {
  me: UseQueryResult<Me>;
  state: ListState;
  establishments: Establishment[] | undefined;
  selected: Establishment | undefined;
  onSelect: (establishment: Establishment) => void;
  onNavigate?: () => void;
  /** The drawer's close button, next to the logo. */
  closeButton?: ReactNode;
}

/** Sidebar content, used persistently from 1024px and inside the drawer below. */
export function Sidebar({
  me,
  state,
  establishments,
  selected,
  onSelect,
  onNavigate,
  closeButton,
}: SidebarProps) {
  const atOverview = useMatch(paths.overview) !== null;
  // A new conversation and a saved one are both Ask the Regulation.
  const atNew = useMatch(paths.ask) !== null;
  const atConversation = useMatch(routePatterns.conversation) !== null;
  // Each section's route, list and detail alike (Settings has Profiles only).
  const atDocuments = useMatch(routePatterns.documents) !== null;
  const atInspections = useMatch(routePatterns.inspections) !== null;
  const atSettings = useMatch(routePatterns.profiles) !== null;
  const items = [
    {
      to: paths.overview,
      label: copy.shell.overview,
      Icon: House,
      shown: can(me.data, permissions.inspections.read) || can(me.data, permissions.documents.read),
      active: atOverview,
    },
    {
      to: paths.ask,
      label: copy.shell.askTheRegulation,
      Icon: MessagesSquare,
      shown: can(me.data, permissions.chat.ask),
      active: atNew || atConversation,
    },
    {
      to: paths.documents,
      label: copy.shell.documents,
      Icon: FileText,
      shown: can(me.data, permissions.documents.read),
      active: atDocuments,
    },
    {
      to: paths.inspections,
      label: copy.shell.inspections,
      Icon: ClipboardCheck,
      shown: can(me.data, permissions.inspections.read),
      active: atInspections,
    },
    {
      to: paths.profiles,
      label: copy.shell.settings,
      Icon: Settings,
      shown: can(me.data, permissions.profiles.read),
      active: atSettings,
    },
  ].filter((item) => item.shown);
  return (
    <>
      <div className="flex items-center justify-between">
        <Logo className="h-7" />
        {closeButton}
      </div>
      <div className="mt-5 flex flex-col gap-2">
        <TenantLabel me={me} />
        {(me.isPending || can(me.data, permissions.establishments.read)) && (
          <EstablishmentSwitcher
            state={state}
            establishments={establishments}
            selected={selected}
            onSelect={onSelect}
          />
        )}
      </div>
      {me.isPending ? (
        <div aria-busy="true" className="mt-5 flex flex-col gap-1">
          <span className="sr-only">{copy.shell.loadingNavigation}</span>
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
        </div>
      ) : (
        items.length > 0 && (
          <nav aria-label={copy.shell.navLabel} className="mt-5">
            <ul className="flex flex-col gap-1">
              {items.map(({ to, label, Icon, active }) => (
                <li key={to}>
                  <Link
                    to={to}
                    onClick={onNavigate}
                    aria-current={active ? "page" : undefined}
                    className={`flex h-9 items-center gap-3 rounded-control px-3 text-body font-medium ${
                      active ? "bg-teal text-text-primary" : "text-text-secondary hover:bg-elevated"
                    }`}
                  >
                    <Icon aria-hidden="true" className="size-4.5 shrink-0" />
                    {label}
                  </Link>
                </li>
              ))}
            </ul>
          </nav>
        )
      )}
      <div className="flex-1" />
      <UserMenu profiles={me.data?.profiles} />
    </>
  );
}

/** One tenant per user: a static label. Hidden while /me has no tenantName (before #52). */
function TenantLabel({ me }: { me: UseQueryResult<Me> }) {
  if (me.isPending) return <Skeleton className="h-4 w-32" />;
  if (!me.data?.tenantName) return null;
  return (
    <p className="flex items-center gap-2 text-body font-medium">
      <Building2 aria-hidden="true" className="size-4 shrink-0 text-text-secondary" />
      <span className="sr-only">{copy.shell.organization}</span>
      <span className="truncate">{me.data.tenantName}</span>
    </p>
  );
}

function UserMenu({ profiles }: { profiles: ProfileSummary[] | undefined }) {
  const { profile, signOut } = useSession();
  // Without a name the email takes its place and is not repeated below it.
  const name = profile.name ?? profile.email ?? "";
  const email = profile.name ? profile.email : undefined;

  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger
        aria-label={name ? format(copy.shell.accountMenu, { name }) : copy.shell.accountMenuUnnamed}
        className="group flex w-full items-center gap-3 rounded-control p-2 text-left hover:bg-elevated"
      >
        <Avatar name={name} />
        <span className="min-w-0 flex-1">
          <span className="block truncate text-body font-medium">{name}</span>
          {/* Muted only on the sidebar: on the hover surface (--bg-elevated) it would fail contrast. */}
          {email && (
            <span className="block truncate text-caption text-text-muted group-hover:text-text-secondary">
              {email}
            </span>
          )}
        </span>
        <ChevronDown aria-hidden="true" className="size-4 shrink-0 text-text-secondary" />
      </DropdownMenu.Trigger>
      <DropdownMenu.Portal>
        <DropdownMenu.Content
          side="top"
          align="start"
          sideOffset={8}
          className="z-50 w-(--radix-dropdown-menu-trigger-width) rounded-control border border-border bg-elevated p-2"
        >
          <DropdownMenu.Label className="px-2 pb-2">
            <span className="block truncate text-body font-medium">{name}</span>
            {email && (
              <span className="block truncate text-small text-text-secondary">{email}</span>
            )}
            {profiles && profiles.length > 0 && (
              <span className="block text-small text-text-secondary">
                {sortProfiles(profiles)
                  .map((profile) => profile.name)
                  .join(", ")}
              </span>
            )}
          </DropdownMenu.Label>
          <DropdownMenu.Separator className="-mx-2 my-1 h-px bg-inset" />
          <DropdownMenu.Item
            onSelect={() => {
              forgetEstablishments();
              void signOut();
            }}
            className="flex h-9 cursor-default items-center gap-2 rounded-sm px-2 text-body select-none focus-visible:-outline-offset-2 data-highlighted:bg-inset"
          >
            <LogOut aria-hidden="true" className="size-4 shrink-0" />
            {copy.shell.signOut}
          </DropdownMenu.Item>
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>
  );
}
