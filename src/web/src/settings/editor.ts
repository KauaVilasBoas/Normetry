import { useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { useNavigate } from "react-router";
import { permissions, problemCodes } from "../api/contract";
import {
  profileKey,
  profilePath,
  profilePermissionsPath,
  profilesKey,
  profilesPath,
  usePermissionCatalog,
  useProfile,
  userProfilePath,
  usersKey,
  type Profile,
  type ProfileListItem,
  type TenantUser,
} from "../api/profiles";
import {
  can,
  meKey,
  queryFailed,
  queryNotFound,
  type Me,
  type ProfileSummary,
} from "../api/queries";
import { useSend } from "../api/send";
import { copy, format } from "../copy";
import { paths } from "../routes";
import { focusById } from "../ui/focus";
import { sameSet, togglePermission } from "./profiles";

export const EDITOR_TITLE_ID = "profile-title";

/**
 * The profile editor's state: the profile (the list's row while it loads) and the catalog, a
 * local draft of its permissions that Save sends whole, and Rename and Delete, sent from their
 * dialogs (`closed` when one closes). Owner is read-only, and so is every profile without
 * `profiles.manage`.
 */
export function useProfileEditor({
  id,
  known,
  me,
  onDeleted,
  announce,
}: {
  id: string | undefined;
  known: ProfileListItem | undefined;
  me: Me | undefined;
  onDeleted: (name: string) => void;
  announce: (text: string) => void;
}) {
  const queryClient = useQueryClient();
  const { send } = useSend();
  // Rename and Delete happen inside their dialogs (one open at a time).
  const inDialog = useSend({ inside: true });
  const readable = can(me, permissions.profiles.read);
  const canManage = can(me, permissions.profiles.manage);
  const profile = useProfile(id, readable);
  const catalog = usePermissionCatalog(readable);
  const [draft, setDraft] = useState<string[]>();
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string>();

  const shown = profile.data ?? known;
  const name = shown?.name;
  const system = shown?.systemKey != null;
  const grantsAll = shown?.grantsAll ?? false;
  const notFound = queryNotFound(profile);
  const failed = !notFound && (queryFailed(profile) || queryFailed(catalog));
  const saved = profile.data?.permissions ?? [];
  const granted = draft ?? saved;
  const dirty = draft !== undefined && !sameSet(draft, saved);
  const path = profilePath(id ?? "");

  // Names show in the users' tags and the account menu; counts in the list.
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: profilesKey });
    void queryClient.invalidateQueries({ queryKey: usersKey });
    if (me?.profiles.some((held) => held.id === id))
      void queryClient.invalidateQueries({ queryKey: meKey });
  };

  // Only the checkboxes call it, and they show once the catalog has loaded.
  const toggle = (permission: string, checked: boolean) => {
    if (catalog.data) setDraft(togglePermission(granted, permission, checked, catalog.data));
  };

  const save = async () => {
    if (!dirty || saving || id === undefined || name === undefined) return;
    setSaving(true);
    setSaveError(undefined);
    const result = await send(profilePermissionsPath(id), {
      method: "PUT",
      json: { permissions: granted },
    });
    if (result.ok) {
      queryClient.setQueryData(profileKey(id), (await result.response.json()) as Profile);
      setDraft(undefined);
      refresh();
      announce(format(copy.profiles.saved, { name }));
    } else {
      setSaveError(
        result.status === 403 ? copy.settings.denied : format(copy.profiles.saveError, { name }),
      );
    }
    setSaving(false);
  };

  const rename = async (next: string) => {
    const result = await inDialog.send(path, { method: "PATCH", json: { name: next } });
    if (result.ok && id !== undefined) {
      queryClient.setQueryData<Profile>(profileKey(id), (old) => old && { ...old, name: next });
      refresh();
      announce(format(copy.profiles.renamed, { name: next }));
    }
    return result;
  };

  /** Resolves to the refusal to show, or undefined once the profile is gone. */
  const remove = async () => {
    const result = await inDialog.send(path, { method: "DELETE" });
    // 404: it is already gone, which is what the user asked for.
    if (!result.ok && result.status !== 404)
      return result.status === 403 ? copy.settings.denied : copy.profiles.deleteError;
    queryClient.setQueryData<ProfileListItem[]>(profilesKey, (list) =>
      list?.filter((item) => item.id !== id),
    );
    queryClient.removeQueries({ queryKey: profileKey(id ?? "") });
    refresh();
    onDeleted(name ?? "");
    return undefined;
  };

  // Try again refetches both; once both load, the heading takes focus from the button.
  const retry = () => {
    void Promise.all([profile.refetch(), catalog.refetch()]).then((results) => {
      if (results.every((result) => result.isSuccess)) focusById(EDITOR_TITLE_ID);
    });
  };

  return {
    canManage,
    catalog: catalog.data,
    name,
    system,
    grantsAll,
    notFound,
    failed,
    retrying: failed && (profile.isFetching || catalog.isFetching),
    loading: !notFound && !failed && (!profile.data || !catalog.data),
    editable: canManage && !grantsAll,
    granted,
    dirty,
    caption: !canManage
      ? copy.profiles.readOnlyCaption
      : system && !grantsAll
        ? copy.profiles.systemCaption
        : undefined,
    saving,
    saveError,
    toggle,
    save,
    rename,
    remove,
    retry,
    closed: inDialog.closed,
  };
}

/**
 * New profile (`NameDialog`): the created profile opens at once, without a fetch (it grants
 * nothing), once the dialog has closed (`openCreated`; below 768px the list, and the dialog with
 * it, goes away). `focus` moves focus after the navigation.
 */
export function useCreateProfile({
  announce,
  focus,
}: {
  announce: (text: string) => void;
  focus: (id: string) => void;
}) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  // Create happens inside its dialog.
  const { send, closed } = useSend({ inside: true });
  const created = useRef<string>(undefined);

  const create = async (name: string) => {
    const result = await send(profilesPath, { method: "POST", json: { name } });
    if (result.ok) {
      const { id } = (await result.response.json()) as { id: string };
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

  const openCreated = () => {
    if (created.current === undefined) return;
    void navigate(paths.profile(created.current));
    created.current = undefined;
    focus(EDITOR_TITLE_ID);
  };

  return { create, openCreated, closed };
}

/**
 * A user's profiles menu: each toggle gives or takes one profile. A toggle in flight shows the
 * target state (`stateOf`); a refusal reverts it and shows as `error`, cleared when the menu
 * closes (`openChange`, which also tells `onOpenChange`).
 */
export function useProfileToggle({
  user,
  name,
  onOpenChange,
}: {
  user: TenantUser;
  name: string;
  onOpenChange: (open: boolean) => void;
}) {
  const queryClient = useQueryClient();
  const { send, closed } = useSend({ inside: true });
  // Profile id → the state its request is setting.
  const [pending, setPending] = useState<Record<string, boolean>>({});
  const [error, setError] = useState<string>();
  const held = new Set(user.profiles.map((profile) => profile.id));

  const stateOf = (profile: ProfileSummary) => {
    const inFlight = profile.id in pending;
    return { inFlight, checked: inFlight ? pending[profile.id] : held.has(profile.id) };
  };

  const toggle = async (profile: ProfileSummary) => {
    if (profile.id in pending) return;
    const on = !held.has(profile.id);
    setError(undefined);
    setPending((current) => ({ ...current, [profile.id]: on }));
    const result = await send(userProfilePath(user.id, profile.id), {
      method: on ? "PUT" : "DELETE",
    });
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

  const openChange = (open: boolean) => {
    onOpenChange(open);
    if (open) return;
    setError(undefined);
    closed();
  };

  return { error, stateOf, toggle, openChange };
}
