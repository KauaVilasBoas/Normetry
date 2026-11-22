import { useQueryClient, type UseQueryResult } from "@tanstack/react-query";
import { ArrowLeft, ChevronRight, Plus } from "lucide-react";
import { useRef } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { permissions } from "../api/contract";
import {
  profileKey,
  profilesKey,
  useProfiles,
  type Profile,
  type ProfileListItem,
} from "../api/profiles";
import { can, queryFailed, type Me } from "../api/queries";
import { useSend } from "../api/send";
import { copy, format } from "../copy";
import { useAnnouncer } from "../ui/announcer";
import { focusById, useFocusAfterCommit, useRefocusWhenGone } from "../ui/focus";
import { media, useMediaQuery } from "../ui/media";
import { Button, ErrorMessage, PoliteRegion, Skeleton, TabStrip, Tag } from "../ui/primitives";
import { NameDialog } from "./NameDialog";
import { EDITOR_TITLE_ID, ProfileEditor } from "./ProfileEditor";
import { countLabel, sortProfiles } from "./profiles";
import { UsersCard } from "./UsersCard";

const PROFILES_TITLE_ID = "profiles-title";

/**
 * Settings › Profiles (spec profiles-settings.md). From 768px: the profile list beside the
 * editor, users below. Below: `/settings/profiles` is the list and the users,
 * `/settings/profiles/:profileId` the editor alone. Renders while /me loads (skeletons); a failed
 * /me shows in the list card with Try again.
 */
export function ProfilesScreen({ me }: { me: UseQueryResult<Me> }) {
  const { profileId } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  // Create happens inside its dialog.
  const { send, closed } = useSend({ inside: true });
  const wide = useMediaQuery(media.list);
  const canManage = can(me.data, permissions.profiles.manage);
  const profiles = useProfiles(can(me.data, permissions.profiles.read));
  const sorted = sortProfiles(profiles.data ?? []);
  // From 768px, the first profile (Owner) when none is chosen.
  const selectedId = profileId ?? (wide ? sorted[0]?.id : undefined);

  // A failed background refetch keeps the cached /me: only a /me never loaded counts.
  const meFailed = queryFailed(me) && !me.data;
  const listFailed = meFailed || queryFailed(profiles);
  const listRetrying = listFailed && (me.isFetching || profiles.isFetching);
  const showList = wide || profileId === undefined;
  const showEditor =
    !meFailed && (profileId !== undefined || (wide && (selectedId !== undefined || !listFailed)));

  const focus = useFocusAfterCommit();

  // Losing profiles.manage (a refetched /me) removes Save, the dialogs and the menus, maybe the
  // focused one. The users card moves focus first for its menus; anything else lands here.
  useRefocusWhenGone(canManage, EDITOR_TITLE_ID, PROFILES_TITLE_ID);

  const { announcements, announce } = useAnnouncer();

  const created = useRef<string>(undefined);
  const create = async (name: string) => {
    const result = await send("/profiles", { method: "POST", json: { name } });
    if (result.ok) {
      const { id } = (await result.response.json()) as { id: string };
      // A new profile grants nothing, so it opens without a fetch.
      queryClient.setQueryData<Profile>(profileKey(id), {
        id,
        name,
        systemKey: null,
        grantsAll: false,
        permissions: [],
      });
      queryClient.setQueryData<ProfileListItem[]>(profilesKey, (list = []) => [
        ...list,
        { id, name, systemKey: null, grantsAll: false, permissionCount: 0 },
      ]);
      void queryClient.invalidateQueries({ queryKey: profilesKey });
      created.current = id;
      announce(format(copy.profiles.created, { name }));
    }
    return result;
  };

  // Once the dialog has closed (below 768px the list, and the dialog with it, goes away).
  const openCreated = () => {
    if (created.current === undefined) return;
    void navigate(`/settings/profiles/${created.current}`);
    created.current = undefined;
    focus(EDITOR_TITLE_ID);
  };

  // Try again: once it loads the button goes away, so focus moves to the heading.
  const retryList = async () => {
    const result = meFailed ? await me.refetch() : await profiles.refetch();
    if (result.isSuccess) focusById(PROFILES_TITLE_ID);
  };

  return (
    <div className="flex flex-col">
      {showList ? (
        <TabStrip
          label={copy.settings.sections}
          tabs={[{ to: "/settings/profiles", label: copy.settings.profilesTab, active: true }]}
        />
      ) : (
        <Link
          to="/settings/profiles"
          onClick={() => focus(PROFILES_TITLE_ID)}
          className="-mx-2 mb-3 inline-flex h-9 items-center gap-1.5 self-start rounded-control px-2 text-body font-medium text-mint focus-visible:-outline-offset-2"
        >
          <ArrowLeft aria-hidden="true" className="size-4" />
          {copy.profiles.title}
        </Link>
      )}

      <div className="flex flex-col gap-4 md:flex-row md:items-start">
        {showList && (
          <section
            aria-labelledby={PROFILES_TITLE_ID}
            aria-busy={(!listFailed && profiles.isPending) || listRetrying}
            className="w-full rounded-card border border-border bg-card motion-safe:animate-fade-in md:w-60 md:shrink-0"
          >
            <div className="p-4">
              <h2 id={PROFILES_TITLE_ID} tabIndex={-1} className="text-title font-semibold">
                {copy.profiles.title}
              </h2>
              {canManage && (
                <NameDialog
                  trigger={
                    <Button variant="secondary" className="mt-3 w-full">
                      <Plus aria-hidden="true" className="size-4" />
                      {copy.profiles.newProfile}
                    </Button>
                  }
                  title={copy.profiles.newTitle}
                  submitLabel={copy.profiles.create}
                  submittingLabel={copy.profiles.creating}
                  onSubmit={create}
                  onClosed={closed}
                  onClosedAfterSave={openCreated}
                />
              )}
            </div>
            <div className="p-2 pt-0">
              {listFailed ? (
                <div className="px-2 pb-2">
                  <ErrorMessage
                    title={copy.profiles.errorTitle}
                    body={copy.profiles.errorBody}
                    retryLabel={copy.settings.tryAgain}
                    retrying={listRetrying}
                    onRetry={() => void retryList()}
                  />
                </div>
              ) : profiles.isPending ? (
                <>
                  <span className="sr-only">{copy.profiles.loading}</span>
                  <ul aria-hidden="true">
                    {[0, 1, 2, 3].map((row) => (
                      <li key={row} className="flex min-h-12 flex-col justify-center gap-2 px-3">
                        <Skeleton className="h-3 w-3/5" />
                        <Skeleton className="h-2.5 w-3/10" />
                      </li>
                    ))}
                  </ul>
                </>
              ) : (
                <ul>
                  {sorted.map((profile) => {
                    const current = profile.id === selectedId;
                    return (
                      <li key={profile.id}>
                        <Link
                          to={`/settings/profiles/${profile.id}`}
                          onClick={() => focus(EDITOR_TITLE_ID)}
                          aria-current={current ? "page" : undefined}
                          className={`flex min-h-12 items-center gap-2 rounded-control px-3 py-1.5 focus-visible:-outline-offset-2 ${
                            current ? "bg-teal" : "hover:bg-elevated"
                          }`}
                        >
                          <span className="min-w-0 flex-1">
                            <span className="flex items-center gap-2">
                              <span className="min-w-0 flex-1 truncate text-body font-medium">
                                {profile.name}
                              </span>
                              {profile.systemKey && (
                                <>
                                  {" "}
                                  <Tag>{copy.profiles.system}</Tag>
                                </>
                              )}
                            </span>{" "}
                            {/* On --teal the caption stays white: secondary text there is 3.6:1. */}
                            <span
                              className={`block text-caption tabular-nums ${current ? "text-text-primary" : "text-text-secondary"}`}
                            >
                              {countLabel(profile)}
                            </span>
                          </span>
                          {!wide && (
                            <ChevronRight
                              aria-hidden="true"
                              className="size-4 shrink-0 text-text-secondary"
                            />
                          )}
                        </Link>
                      </li>
                    );
                  })}
                </ul>
              )}
            </div>
          </section>
        )}

        {showEditor && (
          <ProfileEditor
            key={selectedId}
            id={selectedId}
            known={sorted.find((profile) => profile.id === selectedId)}
            me={me.data}
            onBack={() => focus(PROFILES_TITLE_ID)}
            onDeleted={(name) => {
              void navigate("/settings/profiles");
              focus(PROFILES_TITLE_ID);
              announce(format(copy.profiles.deleted, { name }));
            }}
            announce={announce}
          />
        )}
      </div>

      {showList && !meFailed && <UsersCard me={me.data} profiles={sorted} />}

      <PoliteRegion announcements={announcements} />
    </div>
  );
}
