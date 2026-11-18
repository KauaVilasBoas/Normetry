# Spec: Documents screen (#88)

## 1. Navigation and routes

- Nav item "Documents" (`FileText`, `/documents`, needs `documents.read`), after Ask the Regulation (Spec 1 §1).
- Routes: `/documents` (list) and `/documents/:documentId` (detail). Both are establishment-scoped. The user never picks one to start: the remembered establishment, else the first in the list, is selected, and the switcher changes it. Main shows the establishments card only when the list is empty or failed, and the lock card when the account has no organization.
- Top bar `h1`: "Documents" on both routes.
  - `<title>`: "Documents · Normetry", and on the detail page "{title} · Documents · Normetry".
- **Changing establishment** in the switcher navigates to `/documents`.
- **Opening a detail URL** for a document of another establishment in the same tenant selects that establishment once, as the chat does for conversations.

## 2. Layout

### List, from 768px

One card fills the width; there is no page header.
- The card header has the establishment name as `h2`, a count, and the **primary** "Upload document" on the right.
- The connection notice appears only when needed.
- Below it, a full-bleed table.

```
 Documents                                                        (top bar, 56px)
 ┌──────────────────────────────────────────────────────────────────────────────┐
 │ Bistro Lisboa                                         [ ⇪ Upload document ]  │
 │ 12 documents                                                                 │
 │ ┌ ◌ Live updates paused. Reconnecting…                                     ┐ │ (only while disconnected)
 │ DOCUMENT                        TYPE      STATUS             VERSION  UPDATED│ (inset header)
 │ Cold storage SOP                SOP       (✓ Indexed)        v3       Nov 14 │
 │ cold-storage-sop.pdf                                                         │
 │ Walk-in temperature log         Log       (◌ Processing)     v4       10:42  │
 │ walk-in-temps-oct.csv                     Version 5                          │
 │ Food business license           License   (◆ Failed)         —        Nov 2  │
 │ food-license-2026.pdf                     No text layer                      │
 │ Pest control report             Report    (◷ Waiting)        —        10:44  │
 │ pest-control-oct.docx                                                        │
 └──────────────────────────────────────────────────────────────────────────────┘
```

### List, at 375px

The header wraps and the button becomes full width (`w-full sm:w-auto`). The Type, Version and Updated columns are hidden (`hidden md:table-cell`). The title cell's second line becomes a meta line.

```
┌─────────────────────────────┐
│ [≡] Documents               │
│┌───────────────────────────┐│
││ Bistro Lisboa             ││
││ 12 documents              ││
││ [   ⇪ Upload document    ]││
││ DOCUMENT          STATUS  ││
││ Cold storage SOP (✓ Index…││
││ SOP · v3 · Nov 14         ││
││ Walk-in tempera… (◌ Proce…││
││ Log · v4 · 10:42  Version 5│
││ Food business l… (◆ Faile…││
││ License · — · Nov 2 No te…││
│└───────────────────────────┘│
```

(The sketch truncates the pills. In the build, the Status column keeps the whole pill, and the title truncates instead.)

### Detail, from 768px

- **Page header:** the back link, then the title (22/600), then a meta line.
  - The actions sit on the right: **secondary** "Delete", then **primary** "Upload new version", the order DESIGN.md §4 uses for a page header.
- **Versions card:** its title, then a status notice when the newest version isn't the current one, then the table.

```
 Documents
 ← Documents
 Cold storage SOP                                [Delete] [⇪ Upload new version]
 SOP · Bistro Lisboa                                          (aria-disabled, 50%)
 ┌ Versions ────────────────────────────────────────────────────────────────────┐
 │ ┌ ◌ Version 4 is processing.                                              ┐ │
 │ │   Version 3 stays in use until version 4 is indexed.                    │ │
 │ │   You can upload another version when this one finishes.                │ │
 │ └─────────────────────────────────────────────────────────────────────────┘ │
 │ VERSION          FILE                          STATUS            UPLOADED   │
 │ v4               cold-storage-sop-v4.pdf       (◌ Processing)    10:42      │
 │                  1.8 MB                                          Ana Vieira │
 │ v3  [Current]    cold-storage-sop.pdf          (✓ Indexed)       Nov 14     │
 │                  2.4 MB                                          Ana Vieira │
 │ v2               cold-storage-scan.pdf         (◆ Failed)        Nov 2      │
 │                  6.1 MB                        No text layer     Marco Costa│
 └──────────────────────────────────────────────────────────────────────────────┘
```

### Detail, at 375px

The actions move to their own row under the meta line, in the same order and side by side. The table keeps Version and Status. File and Uploaded become two caption lines under the version number.

```
┌─────────────────────────────┐
│ [≡] Documents               │
│ ← Documents                 │
│ Cold storage SOP            │
│ SOP · Bistro Lisboa         │
│ [Delete][⇪ Upload new ver]│
│┌ Versions ─────────────────┐│
││ ◌ Version 4 is processing.││
││   Version 3 stays in use… ││
││ VERSION          STATUS   ││
││ v4             (◌ Process…││
││ cold-storage-sop-v4.pdf … ││
││ 10:42 · Ana Vieira        ││
││ v3 [Current]   (✓ Indexed)││
│└───────────────────────────┘│
```

### Upload dialog

Centered, `w-[calc(100%-2rem)] max-w-120`, scrolls inside itself when the screen is short.

```
┌ Upload document ─────────────────────────────┐
│ Add a document to Bistro Lisboa.             │
│ ┌ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┐ │
│ │                  ⇪                       │ │   empty
│ │      Choose a file or drop it here       │ │
│ │ PDF, DOCX, TXT or CSV · up to 10 MB and  │ │
│ │ 100 pages                                │ │
│ └ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┘ │
│ Title                                        │
│ [ Cold storage SOP                        ]  │
│ Type                                         │
│ [ Choose a type                         ⌄ ]  │
│                          [Cancel] [Upload]   │
└──────────────────────────────────────────────┘
 Once a file is chosen, the zone shows:  ▤ cold-storage-sop.pdf · 2.4 MB · Choose another file
```

The new-version dialog has the same zone. It has no Title or Type field, its title is "Upload a new version", and its description changes (§6).

## 3. Components and tokens

| Element | Pattern and tokens |
|---|---|
| Documents card | Card: `rounded-card border border-border bg-card`. Header `p-5 flex flex-wrap items-start justify-between gap-3`. `h2` at `text-title font-semibold`, with `tabIndex={-1}`. Caption `text-small text-text-secondary tabular-nums`. **Primary** `Button` with `Upload` 16. |
| Table | DESIGN.md table pattern, edge to edge in the card. Visually hidden `<caption>`. Header: `thead bg-inset`, `th scope="col"`, `h-9 text-caption font-medium text-text-muted`, first and last cells `px-5`. Rows: `border-t border-border`, `min-h-14`, `py-2.5`, `relative`, `hover:bg-elevated`, no zebra striping. |
| Title cell | A `Link` at 13/500 `text-text-primary`, truncated, made into a stretched link (`after:absolute after:inset-0`) so the whole row is clickable with one tab stop. Focus ring inset. Second line, 12px `text-text-secondary`: the newest version's file name from 768px; below 768px the meta line "{type} · {version} · {date}". |
| Type, Version, Updated cells | Type: 13px `text-text-secondary`. Version: 13px `tabular-nums`, "v3", or "—" when no version is indexed. Updated: `<time>` at 12px `text-text-secondary tabular-nums`, formatted with `shortDate` (move it out of `ConversationList` so both screens use it). |
| Status cell | Status pill, then an optional 11px `text-text-secondary` caption under it (§4). |
| Ingestion status pill (**Proposal**) | DESIGN.md status-pill shape: `inline-flex h-5 items-center gap-1 rounded-pill border px-2 text-caption font-medium`, with a glyph and a label. The variants are in the table below. |
| Inline notice (**Proposal**: the composer's quota notice made a component) | `rounded-sm bg-inset p-3 flex gap-2` inside a card. 16px icon. Title line 13px `font-semibold`. Body 12px `text-text-secondary`. |
| Connection notice | An inline notice under the card header (`mx-5 mb-4`) with `role="status"`. Reconnecting: `Spinner`. Stopped: `WifiOff` 16 `text-text-secondary`, plus a secondary "Reconnect" button. |
| Detail header | Back link: `inline-flex items-center gap-1.5 text-body text-text-secondary hover:text-text-primary`, `ArrowLeft` 16. Title `h2`: `text-greeting font-semibold break-words`, `tabIndex={-1}`. Meta: 13px secondary. Actions: `flex gap-2`, on the right from 640px, on their own row below that. Delete: **secondary** `Button` with `Trash2` 16. Upload new version: **primary** with `Upload` 16. When disabled it uses `aria-disabled`, 50% opacity and no hover change (DESIGN.md disabled control). |
| Versions card | Card, `p-5` header with `h2` "Versions", the inline notice, then the table (same classes as the list). The "Current" Tag (Spec 1) sits next to the version number. The File cell shows the name (13px) and the size (12px secondary, `tabular-nums`). The Uploaded cell shows the date (12px) and the uploader (12px secondary). |
| Upload dialog | Radix `Dialog`: `bg-elevated border border-border rounded-card p-5`, over `bg-scrim`, `motion-safe:animate-fade-in`. Title 15/600. Description 13px `text-text-secondary`. A `<form>` with fields 16px apart. Footer right-aligned: Cancel (`secondary onElevated`), then **primary** Upload. While uploading: `Spinner surface="mint"` and "Uploading…". |
| Drop zone (**Proposal**) | A `<button type="button">` that opens a hidden `<input type="file" accept=".pdf,.docx,.txt,.csv">`. Classes: `w-full min-h-32 flex flex-col items-center justify-center gap-1 rounded-control border border-dashed border-teal-border bg-inset p-4 text-center`. `Upload` 20 `text-text-secondary`. Line 13/500 white. Hint 12px `text-text-secondary`. While a file is dragged over it: `border-solid border-mint bg-teal-deep`. Once a file is chosen, the zone shows a row: `FileText` 20, the name 13/500 (truncated), the size 12px secondary, and "Choose another file" 12/500 `text-mint`. |
| Title field, Type field | The Text field from Spec 1. Type is a native `<select>` with the same field classes (`h-11 rounded-control border-teal-border bg-inset`), `appearance-none`, and a `ChevronDown` 16 at the right. |
| Delete dialog | Alert dialog from DESIGN.md §5. Cancel is focused first; Delete is the primary. |
| Loading, empty, error | `Skeleton` and `Message`, as in Spec 1. |

**Pill variants (Proposal).** Ingestion is not a compliance state. Green would read as "this SOP is compliant", and red as "non-compliant". So the pills reuse DESIGN.md's progress semantics: mint for in progress and done, matching the Checklist pattern. Amber "Attention" is used for Failed.

| Status | Label | Glyph (`aria-hidden`) | Border / text | Fill |
|---|---|---|---|---|
| Uploaded | Waiting | `Clock` 12, `text-text-secondary` | `border-neutral` / `text-text-secondary` | `bg-neutral/10` |
| Processing | Processing | `Spinner` at 12px (add a size option); static under reduced motion | `border-mint` / `text-mint` | `bg-mint/10` |
| Indexed | Indexed | `CircleCheck` 12, `text-mint` | `border-teal-border` / `text-text-primary` | transparent |
| Failed | Failed | ◆ (the Attention glyph) | `border-warning` / `text-warning` | `bg-warning/10` |

`--warning` is in DESIGN.md but not yet in `index.css`. Add it there, with `--color-warning`.

## 4. Status rules

- The **newest version** decides the pill. The **current version** is the newest version that is Indexed; it fills the Version column and gets the "Current" tag.
- **List caption under the pill:**
  - if a current version exists and the newest isn't it: "Version {n}";
  - if the status is Failed: the short reason;
  - if both apply: "Version {n} · {reason}".
- **Detail notice**, chosen by the newest version's status. A Failed version shows the long reason. When the newest version is Indexed there is no notice.

| Newest | Current exists | Icon | Title | Body lines |
|---|---|---|---|---|
| Waiting | yes | `Clock` | Version {n} is waiting to be processed. | Version {c} stays in use until version {n} is indexed. |
| Waiting | no | `Clock` | Version {n} is waiting to be processed. | Normetry can read this document once it's indexed. |
| Processing | yes or no | `Spinner` | Version {n} is processing. | The same lines as Waiting |
| Failed | yes | `CircleAlert` | Version {n} couldn't be indexed. | {long reason} · Version {c} is still in use. |
| Failed | no | `CircleAlert` | Version {n} couldn't be indexed. | {long reason} |

  Each body line is its own paragraph, never one concatenated sentence.
- **While the newest version is Waiting or Processing**, the notice adds "You can upload another version when this one finishes." (shown only with `documents.upload`). "Upload new version" is then `aria-disabled` and points to that line with `aria-describedby`.
- **Failed reasons** (unknown codes use the last row):

| Code | Short | Long |
|---|---|---|
| unsupported format | Unsupported format | This file type isn't supported. Upload a PDF, DOCX, TXT or CSV file. |
| no text layer | No text layer | This PDF has no selectable text, which usually means it's a scan. Upload a PDF exported from the original document. |
| too many pages | Too many pages | This document has more than 100 pages. Split it and upload each part as its own document. |
| too large | Too large | This file is larger than 10 MB. Reduce its size or split it, then upload it again. |
| file missing | File missing | The uploaded file couldn't be found. Upload it again. |
| parse error | Couldn't read the file | The file may be damaged or password-protected. Check that it opens, then upload it again. |
| internal error | Internal error | Something went wrong on our side. Upload the file again later. |
| timed out | Timed out | Processing took too long and was stopped. Upload the file again. |
| blob path outside tenant | Internal error | Something went wrong on our side. Upload the file again later. |
| (unknown) | Couldn't be indexed | Something went wrong while processing this file. Upload it again. |

## 5. Behavior

- **List order:** most recently updated first.
- **Upload.** "Upload document" opens the dialog, with initial focus on the drop zone.
  - **Choosing or dropping a file** prefills Title with the file name minus its extension, unless the user already typed one.
  - **Client checks** match the server: extension `.pdf`, `.docx`, `.txt` or `.csv` (any case), and size within the API's exact byte limit. More than one dropped file is refused.
  - **Page count** can only be checked by the worker. A long file shows up as Failed with "Too many pages".
  - **Required fields:** Title and Type. On submit with errors, focus moves to the first invalid control.
  - **Success (202):** the dialog closes, focus returns to the trigger, the list refetches (the new row appears as Waiting), and a message is announced.
  - **Cancel or Esc** aborts a running upload. If the server already accepted the file, it still appears in the list. A click on the scrim does nothing: it would lose the form and abort the upload. A file dropped beside the zone does nothing either (the browser would otherwise open it and leave the app).
- **Dropping on the list card** (only with `documents.upload`) opens the upload dialog with that file chosen. While a file is dragged over it, the card shows the drop zone's mint border. This is a pointer shortcut only.
- **New version.** "Upload new version" opens the same dialog in new-version mode. On success focus returns to the button, which is now `aria-disabled` because a version is in flight.
- **Delete** opens the alert dialog. On success the page goes to `/documents`, focus moves to the card `h2`, and a message is announced.
- **Live status over SignalR:**
  - The connection follows the selected establishment's group.
  - On a push, update or refetch the list and the open detail.
  - After a reconnect, refetch both.
  - The connection notice shows while the connection is reconnecting or has stopped. "Reconnect" restarts the connection and refetches.
  - "Reconnecting…" shows only after 2 s of reconnecting. The hub closes each connection when its token expires (every 5 minutes) and the client reconnects at once, so without the grace period the notice would flash. "Stopped" shows at once.
  - While the new connection starts, "Reconnect" stays as an `aria-disabled` button with a spinner (the Try again pattern). Once connected the notice goes and focus moves to the card heading.

## 6. States and copy (EN)

| State | List | Detail |
|---|---|---|
| Loading | The card `h2` shows the establishment name (it is known), then a count bar (`h-3 w-24`) and 5 skeleton rows (title bar and line-2 bar, a pill bar `h-5 w-20`). `aria-busy`, with a visually hidden "Loading documents…". | Title bar `h-6 w-64` (or the title, when the list has it), meta bar, then 3 version rows. Visually hidden "Loading document…". |
| Empty, with upload | `FileText` 20 `text-neutral` · "No documents yet" · "Upload SOPs, licenses, logs and reports for {establishment}. PDF, DOCX, TXT or CSV, up to 10 MB." The header's Upload button stays. | — |
| Empty, without upload | "No documents yet" · "Documents uploaded for {establishment} appear here." | — |
| Error | `role="alert"`: "Couldn't load documents" / "Check your connection and try again." and "Try again" (retry pattern from Spec 1 §5). | "Couldn't load this document" with the same body and button. |
| Not found (404) | — | "This document isn't available" / "It may have been deleted." and a secondary "Back to documents". |
| Refused | No `documents.read`: "You don't have access to Documents" (Spec 1 §6). | The same. |
| Disconnected | "Live updates paused. Reconnecting…" / "Live updates stopped. Statuses may be out of date." and "Reconnect". | The same. |

**List**
- Caption: "1 document" / "{count} documents".
- Table caption (visually hidden): "Documents for {establishment}".
- Columns: "Document", "Type", "Status", "Version", "Updated".
- Meta line below 768px: "{type} · {version} · {date}".
- No current version: "—" (`aria-hidden`), with the visually hidden text "No indexed version yet".
- Version: "v3" (`aria-hidden`), with the visually hidden text "Version 3".
- Pill labels: "Waiting", "Processing", "Indexed", "Failed".

**Types**
- In the table: SOP · License · Log · Report · Other.
- In the select: "SOP (standard operating procedure)" · "License or permit" · "Log (temperatures, cleaning, deliveries)" · "Report (lab or audit)" · "Other".
- Placeholder: "Choose a type". Unknown types show the raw value.

**Upload dialog**
- Title "Upload document" · description "Add a document to {establishment}."
- Zone: "Choose a file or drop it here" · hint "PDF, DOCX, TXT or CSV · up to 10 MB and 100 pages" · while dragging "Drop the file here" · once chosen "{file}", "{size}", "Choose another file".
- Fields: "Title", "Type". Buttons: "Cancel", "Upload", "Uploading…".
- Errors:
  - "Choose a file to upload."
  - "“{file}” isn't a PDF, DOCX, TXT or CSV file."
  - "“{file}” is larger than 10 MB."
  - "Drop one file at a time."
  - "Enter a title."
  - "Choose a type."
- Server errors:
  - Content check fails: "“{file}” isn't a valid PDF, DOCX, TXT or CSV file."
  - Document limit: "This establishment has reached its document limit. Delete a document before uploading another."
  - 409, version in flight: "Another version of this document is already being processed. Try again when it finishes." Refetch the detail.
  - 403: "You no longer have permission to do this." Refetch `/me`.
  - Network: "Couldn't upload the file. Check your connection and try again."

**New-version dialog**
- Title "Upload a new version".
- Description: "“{title}” keeps its title and type. Version {c} stays in use until the new one is indexed." Without a current version: "“{title}” keeps its title and type."

**Detail**
- Back link "Documents" · meta "{type} · {establishment}".
- "Delete" (`aria-label` "Delete document") · "Upload new version".
- "Versions" · columns "Version", "File", "Status", "Uploaded" · tag "Current" · an uploader without a display name: "Unknown user".
- Notices: §4.

**Delete dialog**
- "Delete “{title}”?" / "The document and all its versions are removed from {establishment}. This can't be undone."
- Buttons "Delete" and "Cancel" · error "Couldn't delete the document. Try again."

**Announcements** (polite):
- "“{title}” uploaded. It's waiting to be processed."
- "New version uploaded. It's waiting to be processed."
- List: "“{title}” is indexed." / "“{title}” couldn't be indexed: {short reason}."
- Detail: "Version {n} is indexed and in use." / "Version {n} couldn't be indexed: {short reason}."
- "“{title}” deleted."

Sizes use `Intl.NumberFormat(locale, { style: "unit", unit: "megabyte" | "kilobyte", maximumFractionDigits: 1 })`.

## 7. Accessibility

**Focus order**
- List: "Upload document" → "Reconnect" (if shown) → one tab stop per row (the stretched title link).
- Detail: back link → Delete → Upload new version.
- Dialog: drop zone → Title → Type → Cancel → Upload.

**Focus after navigation**
- List to detail: the detail's title `h2`.
- Back link or delete: the list card's `h2`.
- Dialogs return focus to their trigger.

**Row names.** Each title link has `aria-describedby` pointing at its status cell, so it reads "Walk-in temperature log, link, Processing, Version 5". The table keeps `th scope="col"` at every width.

**Drag and drop from the keyboard**
- The drop zone is a native button: Enter or Space opens the system file picker. This is the full keyboard path, and it gives the same result as a drop.
- Dragging is a pointer shortcut and never the only way. The same goes for the list-card drop target, whose keyboard equivalent is "Upload document".
- Once a file is chosen, focus stays on the zone. A polite region **inside the dialog** announces "{file} chosen." It has to be inside, because Radix hides everything outside an open dialog, and a region outside would stay silent.

**Live regions**
- One visually hidden polite region for the Documents section. Mount it above both routes, so a "deleted" message survives the navigation.
- It announces only Indexed and Failed, plus upload and delete confirmations. Waiting and Processing are not announced.
- Pills and table cells are never live regions.
- The connection notice sits in a `role="status"` region that is always mounted (empty while live), so what arrives in it is announced. Dialog errors use `role="alert"`.

**Fields.** Every field has a visible `<label>`. Errors are linked with `aria-describedby`, and the field gets `aria-invalid`. The drop zone points to the format hint and to its error with `aria-describedby`.

**Disabled and busy.** "Upload new version" and Upload while running use `aria-disabled`, so they stay focusable. Containers are `aria-busy` while loading.

**Status never by color alone.** Every pill has its label and glyph. The failure reason is visible text.

**Reduced motion.** No spinning, pulsing or fade. The drag-over highlight changes without a transition.

**Touch.** Rows are at least 56px, fields 44px, buttons 36px (the SPA's existing button height), text actions 24px (WCAG 2.5.8).

## 8. Decisions

1. **Navigation** (accepted): "Documents" (`FileText`) joins the DESIGN.md §4 nav list after Compliance. Today the nav reads Ask the Regulation · Documents · Settings.
2. **DESIGN.md §5 additions** (accepted): Ingestion status pill (the four variants in §3), Drop zone, Inline notice, and a 12px Small ring for use inside pills.
3. **Form select** (accepted): a select used as a form field follows the text field (44px, `--radius-control`). The 8px Dropdown stays the compact filter in card headers.
4. Decisions 1–3 land in DESIGN.md with the web PR.
5. **Expiry** (accepted): the Documents row of DESIGN.md §8 drops "expiry" until the model stores an expiry date.
6. **Skipped:** a byte-level upload progress bar (`fetch` gives no upload progress, and 10 MB uploads fast; add it with XHR when slow networks make it matter) and multi-file upload (the dev seed covers bulk loading).
7. **Expiry dates** (#97): see §9. The model stores the date now, so decision 5 no longer applies: the Documents row of DESIGN.md §8 names the expiry column, the filter and the expiry line again, and DESIGN.md §5 gains the Text action.

## 9. Expiry dates (#97)

A document can carry its last valid day (`expiresOn`, a date with no time), typed by the user at upload or later with `documents.upload`. Nothing extracts it. Dates are read as local dates (`localDate`), and "today" is the browser's.

### States

| Date | List, Expires column | Detail line |
|---|---|---|
| None | "—" (`aria-hidden`, 12px secondary) with the visually hidden "No expiry date" | "No expiry date" 13px secondary, only with `documents.upload` (beside its edit action); without it the line is left out |
| Before today | Attention status pill "Expired" (◆), the Overdue pattern | "Expires {date}" then the pill |
| Today to today + 30 | Time badge "Expires {when}" (`relativeDays`: "today", "tomorrow", "in 10 days") | "Expires {date}" then the badge |
| Later | `<time>` with the date (`calendarDate`), 12px secondary, tabular | "Expires {date}" |

### List

```
 │ Bistro Lisboa                       [ All documents ⌄ ] [ ⇪ Upload document ]  │
 │ 12 documents                                                                    │
 │ DOCUMENT                  TYPE     STATUS        VERSION  EXPIRES                UPDATED│
 │ Food business license     License  (✓ Indexed)   v1       (◆ Expired)            Nov 2  │
 │ Pest control certificate  Other    (✓ Indexed)   v1       [Expires in 10 days]   Nov 14 │
 │ Water analysis            Report   (✓ Indexed)   v2       Feb 16                 Nov 14 │
 │ Cold storage SOP          SOP      (✓ Indexed)   v3       —                      Nov 14 │
```

- **Expires column** between Version and Updated: `hidden md:table-cell`, `px-3 py-2.5`, no wrapping.
- **Below 768px** an expired or expiring row shows its pill or badge on its own line under the meta line (`mt-1 flex md:hidden`). A later date shows only on the detail.
- **Row names.** The title link's `aria-describedby` adds the Expires cell only for an expired or expiring date: "Food business license, link, Indexed, Expired". A bare later date would read as noise.
- **Filter.** `FilterSelect` with `aria-label` "Filter by expiry": "All documents" · "Expired" · "Expiring in 30 days", filtered in the browser with the pill's rule; the API's `expired` / `expiringWithinDays` stay for other callers.
  - It sits in the card header's right group before Upload document (`flex w-full flex-wrap gap-2 sm:w-auto`); below 640px both take the full width.
  - It is hidden while the establishment has no documents and no filter is set.
  - It applies on change. The caption still counts every document.
  - It stays set while a document is open and comes back with the list (the state lives above both routes), and it carries over when the establishment changes. It is not in the URL, and a reload clears it.
  - A successful upload clears the filter.
- **Filtered to nothing:** the `Message` with `ListFilter` 20px `text-neutral`: "No expired documents" / "Documents past their expiry date appear here.", or "No documents expiring soon" / "Documents that expire in the next 30 days appear here.", then a secondary "Show all documents". It clears the filter and moves focus to the card `h2`, since the button goes away.

### Upload dialog

- After Type: a `TextField type="date"` "Expiry date" with the hint "For licenses and certificates. Leave empty if the document doesn't expire." Optional, past dates allowed, `max="9999-12-31"`. Sent as `expiresOn` only when filled.
- Not in the new-version dialog: the document keeps its date.
- Focus order: drop zone → Title → Type → Expiry date → Cancel → Upload.
- A half-typed date (the browser reports `badInput` and an empty value, which would upload without a date) or a server `validation` refusal: the field error "Enter a complete date, or leave the field empty.", and focus moves to the field.

### Detail

```
 ← Documents
 Food business license                              [Delete] [⇪ Upload new version]
 License · Bistro Lisboa
 Expires Nov 15  (◆ Expired)  Edit expiry date
```

- A line under the meta line: `mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-body`. "Expires {date}" in a `<time>`, white, then the pill or badge; or "No expiry date" in secondary text (only with `documents.upload`). While loading, a second skeleton bar (`mt-2 h-3 w-32`) stands for it.
- With `documents.upload`, the line ends with "Edit expiry date", a DESIGN.md Text action (`ui/textAction`, shared with a finding's "Open document"). The header actions stay one secondary and one primary; a third button would not fit one row at 375px.
- Focus order: back link → Edit expiry date → Delete → Upload new version.

### Edit dialog

- `FormDialog` (400px), title "Edit expiry date": the same date field, prefilled, then "Remove date" (a Text action) while the document has a date and the field holds one. Footer: Cancel, then the primary "Save" ("Saving…"), `aria-disabled` until the value changed.
- Save sends `PATCH /establishments/{e}/documents/{d}` with `{ "expiresOn": "YYYY-MM-DD" }`, or `null` when the field was emptied. Remove date empties the field and moves focus to it; Save then sends `null`.
- On 204: the cached detail takes the new date, the detail and every list of the establishment refetch, the dialog closes, and focus returns to "Edit expiry date". Once it has closed, the polite region announces "Expiry date saved." or "Expiry date removed.".
- Refusals: a `validation` problem is the field error above. A 403 is "You no longer have permission to do this." (and `/me` refetches once the dialog closes). Anything else is "Couldn't save the expiry date. Try again.", inline with `role="alert"`.
