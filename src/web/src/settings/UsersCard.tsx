import { useQueryClient } from "@tanstack/react-query";
import { Check, ChevronDown, CircleAlert } from "lucide-react";
import { DropdownMenu } from "radix-ui";
import { useEffect, useRef, useState } from "react";
import { permissions, problemCodes } from "../api/contract";
import {
  can,
  meKey,
  queryFailed,
  usersKey,
  useUsers,
  type Me,
  type ProfileSummary,
  type TenantUser,
} from "../api/queries";
import { copy, format, locale } from "../copy";
import { Avatar, Button, ErrorMessage, Skeleton, Spinner, Tag } from "../ui/primitives";
import { sortProfiles, useSend } from "./profiles";

export const USERS_TITLE_ID = "users-title";

/** As the account menu: a user with no name shows their email instead. */
const userName = (user: TenantUser) => user.displayName ?? user.email ?? "";

/** The tenant's users and their profiles; with `profiles.manage`, a menu to change them. */
export function UsersCard({
  me,
  profiles,
}: {
  me: Me | undefined;
  /** Every profile, sorted, for the menus. */
  profiles: ProfileSummary[];
}) {
  const canManage = can(me, permissions.profiles.manage);
  const users = useUsers(can(me, permissions.profiles.read));
  const title = useRef<HTMLHeadingElement>(null);
  const failed = queryFailed(users);
  const retrying = failed && users.isFetching;
  const rows = [...(users.data ?? [])].sort((a, b) =>
    userName(a).localeCompare(userName(b), locale),
  );

  // A menu that was open when the user lost profiles.manage went away with its button.
  const menuOpen = useRef(false);
  useEffect(() => {
    if (canManage || !menuOpen.current) return;
    menuOpen.current = false;
    title.current?.focus();
  }, [canManage]);

  const reload = async () => {
    if ((await users.refetch()).isSuccess) title.current?.focus();
  };

  return (
    <section
      aria-labelledby={USERS_TITLE_ID}
      aria-busy={(!failed && users.isPending) || retrying}
      className="mt-4 rounded-card border border-border bg-card motion-safe:animate-fade-in"
    >
      <div className="p-5">
        <h2 id={USERS_TITLE_ID} ref={title} tabIndex={-1} className="text-title font-semibold">
          {copy.users.title}
        </h2>
        <p className="mt-1 text-small text-text-secondary">{copy.users.caption}</p>
      </div>
      {failed ? (
        <div className="px-5 pb-5">
          <ErrorMessage
            title={copy.users.errorTitle}
            body={copy.users.errorBody}
            retryLabel={copy.settings.tryAgain}
            retrying={retrying}
            onRetry={() => void reload()}
          />
        </div>
      ) : users.isPending ? (
        <>
          <span className="sr-only">{copy.users.loading}</span>
          <ul aria-hidden="true" className="divide-y divide-border border-t border-border">
            {[0, 1, 2].map((row) => (
              <li key={row} className="flex min-h-15 items-center gap-3 px-5 py-3">
                <Skeleton className="size-8" />
                <span className="flex flex-1 flex-col gap-2">
                  <Skeleton className="h-3 w-2/5" />
                  <Skeleton className="h-2.5 w-3/5" />
                </span>
                {canManage && <Skeleton className="h-9 w-24" />}
              </li>
            ))}
          </ul>
        </>
      ) : (
        <ul className="divide-y divide-border border-t border-border">
          {rows.map((user) => {
            const name = userName(user);
            const email = user.displayName ? user.email : null;
            const held = sortProfiles(user.profiles);
            return (
              <li key={user.id} className="flex min-h-15 items-start gap-3 px-5 py-3">
                <Avatar name={name} />
                <div className="flex min-w-0 flex-1 flex-col gap-2 sm:flex-row sm:gap-3">
                  <div className="min-w-0 sm:w-60 sm:shrink-0">
                    <p className="truncate text-body font-medium">
                      {user.isCurrentUser ? format(copy.users.you, { name }) : name}
                    </p>
                    {email && <p className="truncate text-small text-text-secondary">{email}</p>}
                  </div>
                  {/* Below 640px the tags and the menu share the line under the email. */}
                  <div className="flex min-w-0 flex-1 items-start gap-3">
                    <div className="flex min-w-0 flex-1 flex-wrap gap-1.5 pt-0.5">
                      {held.length > 0 ? (
                        held.map((profile) => <Tag key={profile.id}>{profile.name}</Tag>)
                      ) : (
                        <span className="text-small text-text-secondary">
                          {copy.users.noProfile}
                        </span>
                      )}
                    </div>
                    {canManage && profiles.length > 0 && (
                      <ProfilesMenu
                        user={user}
                        name={name}
                        profiles={profiles}
                        onOpenChange={(open) => (menuOpen.current = open)}
                      />
                    )}
                  </div>
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

/**
 * Each toggle gives or takes one profile and keeps the menu open. A toggle in flight shows the
 * target state with a spinner; a refusal reverts it and shows inside the menu, since Radix hides
 * everything outside an open menu from assistive tech.
 */
function ProfilesMenu({
  user,
  name,
  profiles,
  onOpenChange,
}: {
  user: TenantUser;
  name: string;
  profiles: ProfileSummary[];
  onOpenChange: (open: boolean) => void;
}) {
  const queryClient = useQueryClient();
  const { send, closed } = useSend({ inside: true });
  // Profile id → the state its request is setting.
  const [pending, setPending] = useState<Record<string, boolean>>({});
  const [error, setError] = useState<string>();
  const held = new Set(user.profiles.map((profile) => profile.id));

  const toggle = async (profile: ProfileSummary) => {
    if (profile.id in pending) return;
    const on = !held.has(profile.id);
    setError(undefined);
    setPending((current) => ({ ...current, [profile.id]: on }));
    const result = await send(
      `/users/${encodeURIComponent(user.id)}/profiles/${encodeURIComponent(profile.id)}`,
      { method: on ? "PUT" : "DELETE" },
    );
    if (result.ok) {
      if (user.isCurrentUser) void queryClient.invalidateQueries({ queryKey: meKey });
      await queryClient.invalidateQueries({ queryKey: usersKey });
    } else {
      setError(
        result.code === problemCodes.lastOwner
          ? format(copy.users.lastOwner, { name })
          : result.status === 403
            ? copy.settings.denied
            : format(copy.users.error, { name }),
      );
    }
    setPending((current) => {
      const next = { ...current };
      delete next[profile.id];
      return next;
    });
  };

  return (
    <DropdownMenu.Root
      onOpenChange={(open) => {
        onOpenChange(open);
        if (open) return;
        setError(undefined);
        closed();
      }}
    >
      <DropdownMenu.Trigger asChild>
        {/* The visible "Profiles" is part of the name (WCAG 2.5.3). */}
        <Button variant="secondary" aria-label={format(copy.users.menuLabel, { name })}>
          {copy.users.menu}
          <ChevronDown aria-hidden="true" className="size-4 shrink-0" />
        </Button>
      </DropdownMenu.Trigger>
      <DropdownMenu.Portal>
        <DropdownMenu.Content
          align="end"
          sideOffset={4}
          collisionPadding={16}
          className="z-50 max-w-[calc(100vw-2rem)] min-w-56 rounded-control border border-border bg-elevated p-1"
        >
          <DropdownMenu.Label className="px-2 py-1.5 text-small text-text-secondary">
            {format(copy.users.menuTitle, { name })}
          </DropdownMenu.Label>
          {profiles.map((profile) => {
            const inFlight = profile.id in pending;
            const checked = inFlight ? pending[profile.id] : held.has(profile.id);
            return (
              <DropdownMenu.CheckboxItem
                key={profile.id}
                checked={checked}
                aria-disabled={inFlight || undefined}
                onSelect={(event) => {
                  event.preventDefault();
                  void toggle(profile);
                }}
                className="flex h-9 cursor-default items-center gap-2 rounded-sm px-2 text-body select-none focus-visible:-outline-offset-2 data-highlighted:bg-inset"
              >
                <span className="flex size-4 shrink-0 items-center justify-center">
                  {inFlight ? (
                    <Spinner surface="elevated" />
                  ) : (
                    checked && <Check aria-hidden="true" className="size-4 text-mint" />
                  )}
                </span>
                <span className="truncate">{profile.name}</span>
              </DropdownMenu.CheckboxItem>
            );
          })}
          {error && <DropdownMenu.Separator className="-mx-1 my-1 h-px bg-inset" />}
          {/* A menu may hold only items and groups, so the refusal's assertive live region (what
              role="alert" would be) is a group, present before the text arrives. */}
          <DropdownMenu.Group aria-live="assertive" aria-atomic="true">
            {error && (
              <p className="flex gap-2 px-2 py-1.5 text-small text-text-primary">
                <CircleAlert aria-hidden="true" className="size-4 shrink-0 text-text-secondary" />
                {error}
              </p>
            )}
          </DropdownMenu.Group>
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>
  );
}
