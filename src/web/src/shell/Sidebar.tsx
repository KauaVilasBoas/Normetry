import type { UseQueryResult } from "@tanstack/react-query";
import { Building2, ChevronDown, LogOut, MessagesSquare } from "lucide-react";
import { DropdownMenu } from "radix-ui";
import type { ReactNode } from "react";
import { NavLink } from "react-router";
import type { Establishment, Me } from "../api/queries";
import { useSession } from "../auth/session";
import { copy, format } from "../copy";
import { forgetEstablishments, type ListState } from "../establishments/selection";
import { Logo } from "../ui/Logo";
import { Skeleton } from "../ui/primitives";
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
  return (
    <>
      <div className="flex items-center justify-between">
        <Logo className="h-7" />
        {closeButton}
      </div>
      <div className="mt-5 flex flex-col gap-2">
        <TenantLabel me={me} />
        <EstablishmentSwitcher
          state={state}
          establishments={establishments}
          selected={selected}
          onSelect={onSelect}
        />
      </div>
      <nav aria-label={copy.shell.navLabel} className="mt-5">
        <ul>
          <li>
            <NavLink
              to="/"
              end
              onClick={onNavigate}
              className={({ isActive }) =>
                `flex h-9 items-center gap-3 rounded-control px-3 text-body font-medium ${
                  isActive ? "bg-teal text-text-primary" : "text-text-secondary hover:bg-elevated"
                }`
              }
            >
              <MessagesSquare aria-hidden="true" className="size-4.5 shrink-0" />
              {copy.shell.askTheRegulation}
            </NavLink>
          </li>
        </ul>
      </nav>
      <div className="flex-1" />
      <UserMenu roles={me.data?.roles} />
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

function UserMenu({ roles }: { roles: string[] | undefined }) {
  const { profile, signOut } = useSession();
  // Without a name the email takes its place and is not repeated below it.
  const name = profile.name ?? profile.email ?? "";
  const email = profile.name ? profile.email : undefined;
  const initials = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");

  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger
        aria-label={name ? format(copy.shell.accountMenu, { name }) : copy.shell.accountMenuUnnamed}
        className="group flex w-full items-center gap-3 rounded-control p-2 text-left hover:bg-elevated"
      >
        <span
          aria-hidden="true"
          className="flex size-8 shrink-0 items-center justify-center rounded-pill bg-teal text-small font-semibold"
        >
          {initials}
        </span>
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
            {roles && roles.length > 0 && (
              <span className="block text-small text-text-secondary">{roles.join(", ")}</span>
            )}
          </DropdownMenu.Label>
          <DropdownMenu.Separator className="-mx-2 my-1 h-px bg-border" />
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
