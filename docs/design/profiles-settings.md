# Spec: Profiles tab and permission-aware UI (#83)

## 1. Navigation and routes

**Sidebar navigation**, in this order. An item shows only when the user holds its permission. The nav renders once `/me` arrives; until then it shows two `Skeleton` bars (`h-9 w-full`).

| Item | Route | Lucide icon | Needs | Active on |
|---|---|---|---|---|
| Ask the Regulation | `/` | `MessagesSquare` | `chat.ask` | `/`, `/conversations/*` |
| Documents | `/documents` | `FileText` | `documents.read` | `/documents/*` |
| Settings | `/settings/profiles` | `Settings` | `profiles.read` | `/settings/*` |

All three use the existing nav-item classes (`h-9 rounded-control px-3 text-body font-medium`; active `bg-teal text-text-primary`).

**Routes**
- `/settings` redirects to `/settings/profiles`.
- `/settings/profiles` and `/settings/profiles/:profileId`.
- Settings belongs to the organization, not to an establishment. It renders without the establishment-picker gate. Changing the establishment in the switcher leaves the user on the page.
- Top bar `h1`: "Settings". `<title>`: "Profiles · Settings · Normetry".

**Home (`/`).** A user without `chat.ask` is sent to `/documents` if they hold `documents.read`, otherwise to `/settings/profiles` if they hold `profiles.read`, otherwise to the "no access" state (§6).

## 2. Layout

The breakpoint is `md`, 768px, the same as `media.list` in chat. Main scrolls, unlike the chat screen.

**From 768px.** A row with the Profiles list card (`w-60`, 240px, the same width as the conversation list) and the Editor card (`flex-1`), 16px gap. Below it, the Users card at full width.

- With no `:profileId`, the editor shows the first profile (Owner) and that row is current.
- At 1024px and up, the 220px sidebar sits on the left.

```
 Settings                                                         (top bar, 56px)
 Profiles                                                          tab strip
 ▔▔▔▔▔▔▔▔──────────────────────────────────────────────────────────────────────
 ┌ Profiles ─────────────┐ ┌ Kitchen lead ────────────────────────── [✎] [Del] ┐
 │ [ + New profile      ]│ │ Establishments                                   │
 │ Owner        [System] │ │ [x] View establishments                          │
 │ All permissions       │ │     See the organization's establishments.       │
 │ Manager      [System] │ │ [ ] Manage establishments                        │
 │ 7 permissions         │ │ Documents                                        │
 │ Auditor      [System] │ │ [x] View documents   [x] Upload documents  ...   │
 │ 4 permissions         │ │ Ask the Regulation                               │
 │▐Kitchen lead         ▌│ │ [x] Ask questions                                │
 │▐3 permissions        ▌│ │ Profiles                                         │
 └───────────────────────┘ │ [ ] View profiles    [ ] Manage profiles         │
                           │ ──────────────────────────────────────────────── │
                           │              Unsaved changes   [ Save changes ]  │
                           └──────────────────────────────────────────────────┘
 ┌ Users ──────────────────────────────────────────────────────────────────────┐
 │ People appear here after their first sign-in to Normetry.                   │
 │ ─────────────────────────────────────────────────────────────────────────── │
 │ (AV) Ana Vieira (you)         [Owner]                       [Profiles ⌄]     │
 │      ana.vieira@example.com                                                  │
 │ (MC) Marco Costa              [Manager] [Kitchen lead]      [Profiles ⌄]     │
 │      marco.costa@example.com                                                 │
 └─────────────────────────────────────────────────────────────────────────────┘
```

**Below 768px.** This is route-based master and detail:
- `/settings/profiles` shows the Profiles list card, then the Users card. Each profile row ends with a `ChevronRight` (shown only below 768px).
- `/settings/profiles/:id` shows a back link "Profiles", then the Editor card alone.
- The Save button is full width below 640px.

```
 /settings/profiles                    /settings/profiles/:id
┌─────────────────────────────┐      ┌─────────────────────────────┐
│ [≡] Settings                │      │ [≡] Settings                │
│ Profiles                    │      │ ← Profiles                  │
│ ▔▔▔▔▔▔▔▔────────────────────│      │ ┌─────────────────────────┐ │
│┌ Profiles ─────────────────┐│      │ │ Kitchen lead    [✎] [Del]│ │
││[ + New profile           ]││      │ │ Establishments          │ │
││ Owner     [System]      › ││      │ │ [x] View establishments │ │
││ All permissions           ││      │ │     See the organizat…  │ │
││ Manager   [System]      › ││      │ │ ...                     │ │
││ 7 permissions             ││      │ │ Unsaved changes         │ │
│└───────────────────────────┘│      │ │ [     Save changes    ] │ │
│┌ Users ────────────────────┐│      │ └─────────────────────────┘ │
││(AV) Ana Vieira (you)      ││      └─────────────────────────────┘
││     ana.vieira@exa…       ││
││     [Owner]   [Profiles ⌄]││
│└───────────────────────────┘│
```

## 3. Components and tokens

| Element | Pattern and tokens |
|---|---|
| Tab strip (**Proposal**) | `<nav aria-label="Settings sections">` with links, `h-9`, 13px/500. Active: `text-text-primary`, `border-b-2 border-mint`, `aria-current="page"`. Inactive: `text-text-secondary hover:text-text-primary`. The strip has a 1px `--border` bottom edge and 16px below it. One tab today. |
| Profiles list card | Same as `ConversationList`: `rounded-card border border-border bg-card`. Header `p-4` with an `h2` at `text-title font-semibold`. "New profile" is a **secondary** `Button` (`Plus` 16, `mt-3 w-full`). The list is `p-2 pt-0`. |
| Profile row | The conversation-list row: a `Link`, `min-h-12 rounded-control px-3 py-1.5`. Name 13/500, one line. Caption 11px `text-text-secondary tabular-nums`. A Tag on the right. Hover `bg-elevated`. Current = `bg-teal` with every line in `text-text-primary`. Focus ring inset (`focus-visible:-outline-offset-2`). |
| Tag (**Proposal**) | Used for "System", "Current" and profile names. `h-5 px-2 rounded-sm bg-inset text-caption font-medium text-text-secondary`. Not a status, so no status colors. It reads correctly on `--bg-card` and on `--teal`. |
| Editor card | Card, `p-5`. Header: `h2` (`tabIndex={-1}`, 15/600) plus a Tag. On the right, for custom profiles only, `iconButton` Rename (`Pencil` 20) and Delete (`Trash2` 20). A one-line caption under the heading, `text-small text-text-secondary` (copy in §7). |
| Permission group | `<fieldset>` per area, with a `<legend>` at 12px/600 `text-text-secondary`. Groups are 16px apart. Two columns from 1280px (`xl:grid-cols-2`). |
| Checkbox row (**Proposal**: native checkbox) | `<label>`: `flex min-h-11 items-start gap-3 rounded-control px-2 py-2 hover:bg-elevated`. Native `<input type="checkbox">`, `size-4 mt-0.5 accent-mint`. Label 13/500 white. Description 12px `text-text-secondary`, linked with `aria-describedby`. |
| Read-only row | Same layout with no input. Granted: `Check` 16 `text-text-primary`, white label. Not granted: `Minus` 16 `text-text-secondary`, secondary label. A visually hidden "Granted:" or "Not granted:" precedes the label. |
| Owner note | Inset block: `rounded-sm bg-inset p-3 flex gap-2`, `ShieldCheck` 16 `text-text-secondary`, one 13px white line. |
| Editor footer | `mt-5 border-t border-border pt-4 flex items-center justify-end gap-3`. "Unsaved changes" in 12px `text-text-secondary`, shown only when the draft differs. **Primary** "Save changes" (`w-full sm:w-auto`). While saving: `Spinner surface="mint"` and "Saving…". |
| Users card | Card. Header `p-5` with `h2` "Users" and a 12px secondary caption. The list uses the picker's list classes (`divide-y divide-border border-t border-border`). |
| User row | `flex min-h-15 items-start gap-3 px-5 py-3`. `Avatar` 32. Name 13/500. Email 12px secondary. Tags `flex flex-wrap gap-1.5`. On the right a **secondary** `Button` "Profiles" with `ChevronDown` 16. Below 640px the tags wrap onto a second line under the name. |
| Profile menu | Radix `DropdownMenu`, styled like the account menu: `rounded-control border border-border bg-elevated p-1 min-w-56`. `Label` at 12px secondary. `CheckboxItem`: `h-9 rounded-sm px-2 gap-2 data-highlighted:bg-inset`, focus ring inset. A 16px indicator slot holds `Check` in `text-mint`; while that item's request runs it holds `Spinner surface="elevated"`. The separator is `h-px bg-inset`, because inside an elevated surface dividers use `--bg-inset`. |
| Name dialog (create or rename) | Radix `Dialog` on the Alert dialog surface: `bg-elevated border border-border rounded-card p-5 max-w-100`, centered, over `bg-scrim`. Title 15/600. Footer right-aligned: Cancel (`secondary onElevated`), then the primary. |
| Text field (**Proposal**: port the login spec) | Label 12px `text-text-primary`. Input `h-11 w-full rounded-control border border-teal-border bg-inset px-3 text-field sm:text-body`. Error: `CircleAlert` 16 `text-text-secondary` plus 12px `text-text-primary`. Error state sets `aria-invalid` and `aria-describedby`. No red. |
| Delete profile | Alert dialog exactly as DESIGN.md §5, reusing the pattern of the chat's delete-conversation dialog. |
| Empty, error, refused | The `Message` primitive. Error and empty icons are 20px: `CircleAlert` `text-text-secondary` for errors, `text-neutral` for empty. |
| Loading | The `Skeleton` primitive inside `aria-busy` containers with a visually hidden label. |

## 4. Behavior

- **Selecting a profile** navigates to `/settings/profiles/:id` and moves focus to the editor `h2`. Profiles are sorted Owner, Manager, Auditor, then custom profiles A–Z. Users are sorted by name A–Z. A user with no name shows their email instead, as the account menu does.
- **Editing permissions** changes a local draft. Save sends the whole set in one request.
  - Save is `aria-disabled`, still focusable, when the draft matches the saved set or a save is running.
  - Switching profile or leaving the page discards the draft without a prompt. There are at most 8 checkboxes, so redoing them is cheap.
- **Owner** shows the Owner note and the read-only rows, all granted. It has no Rename, no Delete and no footer.
- **Manager and Auditor** have editable checkboxes but no Rename and no Delete (see §9).
- **Without `profiles.manage`** every profile is read-only. "New profile", the Profiles menu buttons and the footer are all hidden.
- **New profile** opens the name dialog. On success the new profile opens in the editor with no permissions, focus moves to its heading, and a message is announced.
- **Rename** uses the same dialog with the name prefilled. **Delete** opens the alert dialog. After a delete the page goes to `/settings/profiles`, focus moves to the list `h2`, and a message is announced.
- **Assigning and removing profiles**:
  - Each toggle in the menu sends one request. The menu stays open (`onSelect` calls `preventDefault`).
  - While its request runs the item is `aria-disabled` and shows the spinner. On success the users list is refetched.
  - On the lockout refusal the item reverts to checked and the refusal appears inline in the menu (§5). Any other failure reverts the item and shows the generic error in the same place.
- **Changes that touch the current user** (their own assignments, or a profile they hold): refetch `/me` after success. Actions the user just lost then disappear.
  - If the open menu's button disappears this way, focus moves to the Users `h2`.

## 5. States

| State | What shows |
|---|---|
| Loading | List card: 4 skeleton rows (`h-3 w-3/5` and `h-2.5 w-3/10`). Editor: a title bar (`h-4 w-40`), then 4 groups, each a legend bar and two rows. Users: 3 rows, each an avatar `size-8`, two bars and a button bar `h-9 w-24`. Each card is `aria-busy="true"` with a visually hidden "Loading profiles…" or "Loading users…". |
| Empty | Does not occur: there are always 3 system profiles and at least the current user. Do not build one. |
| Error | Inside the affected card, `role="alert"`: `Message` with "Couldn't load profiles" or "Couldn't load users", then a secondary "Try again" (`aria-disabled` with a `Spinner` while retrying, as in `ConversationList`). When the retry succeeds, focus moves to the card `h2`. |
| Profile not found | In the editor: "This profile isn't available" / "It may have been deleted.", then a secondary link-button "Back to profiles". |
| Save, create or delete fails | Inline `role="alert"` text in the editor footer or the dialog, above the buttons. The draft is kept. |
| Lockout refusal (409) | Inside the menu, below a separator, `role="alert"`: `CircleAlert` 16 `text-text-secondary` and a 12px white line, "{name} is the organization's last Owner. Give the Owner profile to someone else first." It clears on the next toggle or when the menu closes. |
| Permission revoked mid-session (403 on an action) | Inline at the action: "You no longer have permission to do this." Then refetch `/me`, which hides the action. |
| Duplicate name (409) | A field error in the name dialog. |
| Refused (direct URL without `profiles.read`) | §6. |

## 6. Permission-aware UI (whole app)

`/me` gains `permissions: string[]` and `profiles` (id and name). While `/me` is pending, every gated action stays hidden so nothing flashes. The server still checks every request.

| Permission | Shows |
|---|---|
| `chat.ask` | Ask the Regulation (nav item and screen) |
| `documents.read` | Documents (nav item, list, detail, live updates) |
| `documents.upload` | "Upload document", "Upload new version", dropping a file on the documents list |
| `documents.delete` | "Delete" on the document detail |
| `establishments.read` | The establishment switcher and picker. Chat and Documents need it. |
| `establishments.manage` | Nothing yet: the screen doesn't exist |
| `profiles.read` | Settings (nav item, read-only Profiles tab) |
| `profiles.manage` | New profile, Rename, Delete, permission checkboxes and Save, the users' Profiles menus |

**Refused and no-access states.** All render in main as a `Message` inside a card (`p-5 max-w-160`, like the picker) with `Lock` 20 `text-neutral`:
- **No permissions at all** (a user with no profile): "You don't have access yet" / "Ask an administrator in your organization to give you a profile, then reload this page." The sidebar shows only the logo, the tenant label and the user card. Do not call `/establishments`.
- **A route without its permission:** "You don't have access to {screen}" / "Ask an administrator in your organization for access." `{screen}` is "Ask the Regulation", "Documents" or "Settings".
- **`documents.read` or `chat.ask` without `establishments.read`:**
  - Hide the switcher and don't call `/establishments`. A 403 from that endpoint must no longer be read as "not linked to an organization"; only a 403 from `/me` means that.
  - The screen shows "You don't have access to establishments" / "Documents and Ask the Regulation work per establishment. Ask an administrator in your organization for access."
- **Account menu:** the `roles` line goes away with the realm roles. It shows the user's profile names from `/me`'s `profiles`.

## 7. Copy (EN)

**Shell**
- Nav items: "Documents", "Settings".
- Tab: "Profiles". Tab nav label: "Settings sections".

**Profiles list**
- Heading "Profiles" · button "New profile" · tag "System".
- Row caption: "All permissions" / "1 permission" / "{count} permissions" / "No permissions".
- Loading "Loading profiles…" · error "Couldn't load profiles" / "Check your connection and try again." / "Try again".

**Editor captions**
- Owner note: "Owner has every permission, including ones added later. It can't be changed or deleted."
- Manager and Auditor: "System profile. You can change its permissions, but you can't delete it."
- Read-only viewer: "You can view profiles but not change them."

**Editor actions and messages**
- Icon buttons: "Rename profile", "Delete profile".
- Footer: "Unsaved changes" · "Save changes" · "Saving…".
- Saved (announced): "{name} saved." · save error "Couldn't save {name}. Try again."
- Hidden prefixes on read-only rows: "Granted:" and "Not granted:".
- Not found: "This profile isn't available" / "It may have been deleted." / "Back to profiles".

**Areas and permissions.** Unknown names fall back to the raw string, grouped by the prefix before the dot.

| Area legend | Permission | Label | Description |
|---|---|---|---|
| Establishments | `establishments.read` | View establishments | See the organization's establishments. |
| | `establishments.manage` | Manage establishments | Add, edit and remove establishments. |
| Documents | `documents.read` | View documents | See documents, their versions and their status. |
| | `documents.upload` | Upload documents | Upload documents and new versions. |
| | `documents.delete` | Delete documents | Delete documents and all their versions. |
| Ask the Regulation | `chat.ask` | Ask questions | Ask about the regulation that applies to an establishment. |
| Profiles | `profiles.read` | View profiles | See profiles, their permissions and who holds them. |
| | `profiles.manage` | Manage profiles | Create, change and delete profiles, and give them to people. |

**Name dialog**
- Titles: "New profile" / "Rename profile".
- Field "Name", with the hint "For example, Kitchen lead."
- Buttons: "Create profile" / "Save" · "Creating…" / "Saving…" · "Cancel".
- Errors: "Enter a name." · "A profile named “{name}” already exists." · "Couldn't save the profile. Try again."
- Announcements: "“{name}” created. Choose its permissions." · "Profile renamed to “{name}”."

**Delete dialog**
- "Delete “{name}”?" / "People who hold this profile lose its permissions. This can't be undone."
- Buttons "Delete" and "Cancel".
- Error "Couldn't delete the profile. Try again." · announced "“{name}” deleted."

**Users**
- Heading "Users" · caption "People appear here after their first sign-in to Normetry."
- Your own row: "{name} (you)" · a user with no profile: "No profile".
- Button "Profiles", with `aria-label` "Change profiles for {name}" (the visible text is part of the name).
- Menu label: "Profiles for {name}".
- Lockout: as in §5 · other errors: "Couldn't change {name}'s profiles. Try again."
- Loading "Loading users…" · error "Couldn't load users".

**Refused states:** as in §6.

## 8. Accessibility

**Focus order (from 768px)**
1. Tab "Profiles".
2. "New profile", then the profile rows. Each row is a link with `aria-current="page"` when selected.
3. Editor: Rename and Delete, the checkboxes area by area, then Save.
4. Users: each row's Profiles button.

Below 768px, the back link "Profiles" comes first on the detail route.

**Focus after navigation**
- Selecting a profile, at any width: the editor `h2`, which has `tabIndex={-1}`.
- The back link: the list `h2`.
- After a delete: the list `h2`.
- Dialogs return focus to their trigger. After a create, focus goes to the new editor `h2` instead.

**Labels**
- Each area is a `fieldset` with a `legend`. Each checkbox is a native input wrapped in its `<label>`, with its description linked by `aria-describedby`.
- The icon buttons carry their `aria-label`s.
- The tags are plain text inside the row link, so a row reads "Manager, System, 7 permissions".

**Menu.** Radix handles the keyboard: arrows move, Space or Enter toggles, Esc closes and returns focus to the button. A screen reader announces `menuitemcheckbox` state changes on its own, so successful toggles need no extra announcement. A pending item is `aria-disabled`.

**Live regions**
- One visually hidden `aria-live="polite"` region per screen, as in `ChatScreen`. It carries saved, created, renamed and deleted messages.
- Refusals and errors use `role="alert"` where they appear. The lockout message sits inside the menu, because Radix hides everything outside an open menu from assistive tech.

**Other**
- Disabled controls use `aria-disabled`, never `disabled` (the one exception is a native checkbox with no `profiles.manage`; that case renders read-only rows instead).
- Every row and checkbox label is at least 44px tall.
- Every interactive element keeps the mint focus ring, inset on rows and menu items.

## 9. Decisions

1. **System profile names.** Manager and Auditor can't be renamed. The server refuses a rename, and the UI shows no Rename for them. The UI matches system profiles by their system key, never by the stored name, so their names can be translated when Portuguese arrives.
2. **`/me`** returns `profiles` (id and name) next to `permissions`. The account menu shows the profile names.
3. **Read dependency** (accepted). Checking any other permission of an area also checks `<area>.read`. Unchecking `<area>.read` unchecks the rest of the area. The server applies the same rule: it adds `<area>.read` whenever any other permission of that area is granted.
4. **DESIGN.md §5 additions** (accepted): Tab strip, Tag, Checkbox (native, 16px, `accent-color: --mint`, inside a 44px label row), Text field (the login spec's field ported to the SPA) and Form dialog (the alert-dialog surface, max width 400; the upload dialog widens it to 480). They land in DESIGN.md with the web PR.
5. **Skipped:** a confirmation when you remove your own `profiles.manage`. The lockout guard keeps the organization safe, and another Owner can restore it. Add it if users trip over this.
