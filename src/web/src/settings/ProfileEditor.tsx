import { useQueryClient } from "@tanstack/react-query";
import { CircleAlert, Pencil, ShieldCheck, Trash2 } from "lucide-react";
import { AlertDialog } from "radix-ui";
import { useRef, useState } from "react";
import { Link } from "react-router";
import { permissions } from "../api/contract";
import {
  profileKey,
  profilesKey,
  usePermissionCatalog,
  useProfile,
  usersKey,
  type Profile,
  type ProfileListItem,
} from "../api/profiles";
import { can, meKey, queryFailed, queryNotFound, type Me } from "../api/queries";
import { useSend } from "../api/send";
import { copy, format } from "../copy";
import { paths } from "../routes";
import { focusById } from "../ui/focus";
import {
  Button,
  Checkbox,
  dialogPanel,
  ErrorMessage,
  iconButton,
  ReadOnlyCheckbox,
  secondaryButton,
  Skeleton,
  Spinner,
  Tag,
} from "../ui/primitives";
import { NameDialog } from "./NameDialog";
import { groupPermissions, sameSet, togglePermission } from "./profiles";

export const EDITOR_TITLE_ID = "profile-title";

/**
 * One profile's permissions by area. Editing changes a local draft that Save sends whole; the
 * draft goes when the profile changes (the screen keys the editor by id). Owner is read-only, and
 * so is every profile without `profiles.manage`.
 */
export function ProfileEditor({
  id,
  known,
  me,
  onBack,
  onDeleted,
  announce,
}: {
  /** Undefined while the list that picks the first profile loads. */
  id: string | undefined;
  /** The list's row, shown while the profile loads. */
  known: ProfileListItem | undefined;
  me: Me | undefined;
  /** "Back to profiles" was chosen (its link navigates). */
  onBack: () => void;
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
  const retrying = failed && (profile.isFetching || catalog.isFetching);
  const loading = !notFound && !failed && (!profile.data || !catalog.data);
  const editable = canManage && !grantsAll;
  const saved = profile.data?.permissions ?? [];
  const granted = draft ?? saved;
  const dirty = draft !== undefined && !sameSet(draft, saved);
  const caption = !canManage
    ? copy.profiles.readOnlyCaption
    : system && !grantsAll
      ? copy.profiles.systemCaption
      : undefined;

  // Names show in the users' tags and the account menu; counts in the list.
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: profilesKey });
    void queryClient.invalidateQueries({ queryKey: usersKey });
    if (me?.profiles.some((held) => held.id === id))
      void queryClient.invalidateQueries({ queryKey: meKey });
  };

  const path = `/profiles/${encodeURIComponent(id ?? "")}`;

  const save = async () => {
    if (!dirty || saving || id === undefined || name === undefined) return;
    setSaving(true);
    setSaveError(undefined);
    const result = await send(`${path}/permissions`, {
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

  return (
    <section
      aria-labelledby={EDITOR_TITLE_ID}
      aria-busy={loading || retrying}
      className="min-w-0 flex-1 rounded-card border border-border bg-card p-5 motion-safe:animate-fade-in"
    >
      <div className="flex items-center gap-2">
        {notFound && (
          <CircleAlert aria-hidden="true" className="size-5 shrink-0 text-text-secondary" />
        )}
        <h2
          id={EDITOR_TITLE_ID}
          tabIndex={-1}
          className="min-w-0 truncate text-title font-semibold"
        >
          {notFound
            ? copy.profiles.notFoundTitle
            : (name ??
              (failed ? (
                copy.profiles.errorTitle
              ) : (
                <>
                  <Skeleton className="my-0.5 h-4 w-40 max-w-full" />
                  <span className="sr-only">{copy.profiles.loading}</span>
                </>
              )))}
        </h2>
        {!notFound && system && <Tag>{copy.profiles.system}</Tag>}
        <span className="flex-1" />
        {canManage && !notFound && !system && name !== undefined && (
          <>
            <NameDialog
              trigger={
                <button
                  type="button"
                  aria-label={copy.profiles.rename}
                  className={`${iconButton} -my-2`}
                >
                  <Pencil aria-hidden="true" className="size-5" />
                </button>
              }
              title={copy.profiles.renameTitle}
              submitLabel={copy.profiles.renameSave}
              submittingLabel={copy.profiles.renameSaving}
              initialName={name}
              onSubmit={rename}
              onClosed={inDialog.closed}
            />
            <DeleteProfile name={name} onDelete={remove} onClosed={inDialog.closed} />
          </>
        )}
      </div>
      {!notFound && caption && <p className="mt-1 text-small text-text-secondary">{caption}</p>}

      {notFound ? (
        <>
          <p className="mt-2 text-body text-text-secondary">{copy.profiles.notFoundBody}</p>
          <Link to={paths.profiles} onClick={onBack} className={`${secondaryButton} mt-4`}>
            {copy.profiles.backToProfiles}
          </Link>
        </>
      ) : failed ? (
        <div className="mt-4">
          <ErrorMessage
            title={copy.profiles.errorTitle}
            body={copy.profiles.errorBody}
            retryLabel={copy.settings.tryAgain}
            retrying={retrying}
            onRetry={() => {
              void Promise.all([profile.refetch(), catalog.refetch()]).then((results) => {
                if (results.every((result) => result.isSuccess)) focusById(EDITOR_TITLE_ID);
              });
            }}
          />
        </div>
      ) : loading || !catalog.data ? (
        <EditorSkeleton label={name !== undefined} />
      ) : (
        <>
          {grantsAll && (
            <p className="mt-4 flex gap-2 rounded-sm bg-inset p-3 text-body">
              <ShieldCheck
                aria-hidden="true"
                className="mt-0.5 size-4 shrink-0 text-text-secondary"
              />
              {copy.profiles.ownerNote}
            </p>
          )}
          <div className="mt-4 flex flex-col gap-4">
            {groupPermissions(catalog.data).map((group) => (
              <fieldset key={group.area} className="min-w-0">
                <legend className="text-small font-semibold text-text-secondary">
                  {group.legend}
                </legend>
                <div className="-mx-2 mt-1 grid xl:grid-cols-2">
                  {group.names.map((permission) => {
                    const text = copy.permissions[permission];
                    const checked = granted.includes(permission);
                    return editable ? (
                      <Checkbox
                        key={permission}
                        label={text?.label ?? permission}
                        description={text?.description}
                        checked={checked}
                        onChange={(event) =>
                          setDraft(
                            togglePermission(
                              granted,
                              permission,
                              event.target.checked,
                              catalog.data,
                            ),
                          )
                        }
                      />
                    ) : (
                      <ReadOnlyCheckbox
                        key={permission}
                        checked={checked}
                        state={checked ? copy.profiles.granted : copy.profiles.notGranted}
                        label={text?.label ?? permission}
                        description={text?.description}
                      />
                    );
                  })}
                </div>
              </fieldset>
            ))}
          </div>
          {editable && (
            <div className="mt-5 flex flex-wrap items-center justify-end gap-3 border-t border-border pt-4">
              {saveError && (
                <p role="alert" className="w-full text-body">
                  {saveError}
                </p>
              )}
              {dirty && (
                <span className="text-small text-text-secondary">{copy.profiles.unsaved}</span>
              )}
              {/* aria-disabled, not disabled: it stays focusable. Dimmed when there is nothing to save. */}
              <Button
                variant="primary"
                aria-disabled={!dirty || saving}
                onClick={() => void save()}
                className={`w-full aria-disabled:hover:bg-mint sm:w-auto ${saving ? "" : "aria-disabled:opacity-50"}`}
              >
                {saving && <Spinner surface="mint" />}
                {saving ? copy.profiles.saving : copy.profiles.save}
              </Button>
            </div>
          )}
        </>
      )}
    </section>
  );
}

/** Four groups, each a legend and two rows. `label`: say "Loading profiles…" (else the heading does). */
function EditorSkeleton({ label }: { label: boolean }) {
  return (
    <>
      {label && <span className="sr-only">{copy.profiles.loading}</span>}
      <div aria-hidden="true" className="mt-4 flex flex-col gap-4">
        {[0, 1, 2, 3].map((group) => (
          <div key={group} className="flex flex-col gap-3">
            <Skeleton className="h-3 w-24" />
            <Skeleton className="h-3 w-2/5" />
            <Skeleton className="h-3 w-1/3" />
          </div>
        ))}
      </div>
    </>
  );
}

/** The header's Delete and its confirmation; no red button (DESIGN.md). */
function DeleteProfile({
  name,
  onDelete,
  onClosed,
}: {
  name: string;
  /** Resolves to the refusal to show, or undefined once deleted (the editor then goes away). */
  onDelete: () => Promise<string | undefined>;
  /** Cancelled: a refusal it showed may now take its trigger away. */
  onClosed: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string>();
  const trigger = useRef<HTMLButtonElement>(null);

  const confirm = async () => {
    setDeleting(true);
    setError(undefined);
    const refusal = await onDelete();
    if (refusal === undefined) return;
    setDeleting(false);
    setError(refusal);
  };

  return (
    <AlertDialog.Root
      open={open}
      onOpenChange={(next) => {
        if (deleting) return;
        setOpen(next);
        setError(undefined);
        if (!next) onClosed();
      }}
    >
      <AlertDialog.Trigger
        ref={trigger}
        aria-label={copy.profiles.delete}
        className={`${iconButton} -my-2 -mr-2`}
      >
        <Trash2 aria-hidden="true" className="size-5" />
      </AlertDialog.Trigger>
      <AlertDialog.Portal>
        <AlertDialog.Overlay className="fixed inset-0 z-40 bg-scrim" />
        <AlertDialog.Content
          // Back to the trigger after Cancel; after a delete the list heading takes focus.
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            if (trigger.current?.isConnected) trigger.current.focus();
          }}
          className={dialogPanel}
        >
          <AlertDialog.Title className="text-title font-semibold">
            {format(copy.profiles.deleteTitle, { name })}
          </AlertDialog.Title>
          <AlertDialog.Description className="mt-2 text-body text-text-secondary">
            {copy.profiles.deleteBody}
          </AlertDialog.Description>
          {error && (
            <p role="alert" className="mt-3 text-body">
              {error}
            </p>
          )}
          <div className="mt-5 flex justify-end gap-2">
            <AlertDialog.Cancel asChild>
              <Button variant="secondary" onElevated>
                {copy.settings.cancel}
              </Button>
            </AlertDialog.Cancel>
            <Button
              variant="primary"
              aria-disabled={deleting}
              onClick={deleting ? undefined : () => void confirm()}
            >
              {deleting && <Spinner surface="mint" />}
              {copy.profiles.deleteConfirm}
            </Button>
          </div>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  );
}
