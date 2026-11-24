import { CircleAlert, Pencil, ShieldCheck, Trash2 } from "lucide-react";
import { Link } from "react-router";
import type { ProfileListItem } from "../api/profiles";
import type { Me } from "../api/queries";
import { copy, format } from "../copy";
import { paths } from "../routes";
import {
  Button,
  Checkbox,
  ConfirmDialog,
  ErrorMessage,
  iconButton,
  ReadOnlyCheckbox,
  secondaryButton,
  Skeleton,
  Spinner,
  Tag,
} from "../ui/primitives";
import { EDITOR_TITLE_ID, useProfileEditor } from "./editor";
import { NameDialog } from "./NameDialog";
import { groupPermissions } from "./profiles";

/**
 * One profile's permissions by area (`useProfileEditor`): the header with Rename and Delete, the
 * grid, and Save. The draft goes when the profile changes (the screen keys the editor by id).
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
  const {
    canManage,
    catalog,
    name,
    system,
    grantsAll,
    notFound,
    failed,
    retrying,
    loading,
    editable,
    granted,
    dirty,
    caption,
    saving,
    saveError,
    toggle,
    save,
    rename,
    remove,
    retry,
    closed,
  } = useProfileEditor({ id, known, me, onDeleted, announce });

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
              onClosed={closed}
            />
            {/* No red button (DESIGN.md). */}
            <ConfirmDialog
              trigger={
                <button
                  type="button"
                  aria-label={copy.profiles.delete}
                  className={`${iconButton} -my-2 -mr-2`}
                >
                  <Trash2 aria-hidden="true" className="size-5" />
                </button>
              }
              title={format(copy.profiles.deleteTitle, { name })}
              body={copy.profiles.deleteBody}
              cancelLabel={copy.settings.cancel}
              confirmLabel={copy.profiles.deleteConfirm}
              onConfirm={remove}
              onClosed={closed}
            />
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
            onRetry={retry}
          />
        </div>
      ) : loading || !catalog ? (
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
            {groupPermissions(catalog).map((group) => (
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
                        onChange={(event) => toggle(permission, event.target.checked)}
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
