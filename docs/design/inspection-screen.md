# Simulated Inspection screen

Issues #104 (run, live progress, failures) and #105 (findings, review, action plan). This spec follows `docs/design/DESIGN.md` and the mock's Simulated Inspection panel: the stepper strip, the radial progress, the checklist and the card language. It reuses the Documents screen's list and detail patterns (`documents-screen.md`) and the chat's source sheet. English only until the PT switch. Quotes keep their original language.

## 1. Navigation and routes

- **Nav item:** "Simulated Inspection", Lucide `ClipboardCheck`, linking to `/inspections`. It needs `inspections.read` and is active on `/inspections/*`. It sits after Documents, so the nav reads Ask the Regulation · Documents · Simulated Inspection · Settings. It uses the existing nav-item classes.
- **Routes:** `/inspections` is the selected establishment's history. `/inspections/:inspectionId` is one inspection: waiting, running, failed or completed. Both are scoped to the establishment. The API path holds the establishment (`/establishments/{e}/inspections/{id}`), so the detail is always fetched for the selected establishment. An inspection of another establishment comes back 404 and shows "not found" (§6). Unlike Documents, the URL can't select the establishment.
- **Top bar `h1`:** "Simulated Inspection" on both routes.
  - `<title>`: "Simulated Inspection · Normetry". On the detail page: "Inspection of {date} · Simulated Inspection · Normetry".
- **Changing establishment** in the switcher navigates to `/inspections` (add it to `sectionRoots`).
- **Home (`/`)** goes to the first screen the user may open, in this order: Ask the Regulation, Documents, Simulated Inspection, Settings. `canOpenAScreen` gains `inspections.read`.
- **Refused states:** the Profiles spec §6 pattern, with `{screen}` = "Simulated Inspection". Without `establishments.read` the screen shows the establishments refusal, whose body now names Simulated Inspection (copy in §6).
- **Permissions:**

| Permission | Shows |
|---|---|
| `inspections.read` | the nav item, history, inspection page, live progress, findings, action plan (read-only) |
| `inspections.run` | "Run inspection", its refusal notices, and the empty-state body that invites a run |
| `inspections.review` | the finding sheet's footer (Accept, Reject, Edit) and the action plan's done checkboxes |

  The Settings permission editor gets a "Simulated Inspection" area with these three permissions (copy in §6). The area read rule (Profiles §9.3) applies as is.

## 2. Layout

Main scrolls, as on Documents. The breakpoints are 640px (`sm`), 768px (`md`) and 1280px (`xl`, the Theme column only).

### History, from 768px

There is one card and no page header, as on the Documents list.
- Card header: the establishment name as `h2`, a count, and the **primary** "Run inspection" on the right.
- Below the header: the run notice and the connection notice, each only when needed.
- Then a full-bleed table.

```
 Simulated Inspection                                             (top bar, 56px)
 ┌──────────────────────────────────────────────────────────────────────────────┐
 │ Bistro São Paulo                                        [ ▷ Run inspection ] │ (aria-disabled, 50%)
 │ 4 inspections                                                                │
 │ ┌ ◌ An inspection is running.                              [View progress] ┐ │
 │ │   You can run another one when it finishes.                              │ │
 │ └──────────────────────────────────────────────────────────────────────────┘ │
 │ REQUESTED                    STATUS               RESULTS                    │ (inset header)
 │ Nov 17, 2026, 10:42 AM       (◌ Running)          —                          │
 │                              27 of 43 checked                                │
 │ Nov 16, 2026, 4:05 PM        (✓ Completed)        6 non-compliant · 4 insuf- │
 │                                                   ficient evidence · 33 comp…│
 │ Nov 16, 2026, 9:12 AM        (◆ Failed)           —                          │
 │                              Processing limit reached                        │
 └──────────────────────────────────────────────────────────────────────────────┘
```

### History, at 375px

The header wraps and the button goes full width (`w-full sm:w-auto`). The Results column is hidden (`hidden md:table-cell`), and a completed row's results become the second line of the Requested cell. The running count and the failure reason stay under the pill in the Status cell at every width.

```
┌─────────────────────────────┐
│ [≡] Simulated Inspection    │
│┌───────────────────────────┐│
││ Bistro São Paulo          ││
││ 4 inspections             ││
││ [    ▷ Run inspection    ]││
││ ◌ An inspection is runni… ││
││   You can run another …   ││
││   [View progress]         ││
││ REQUESTED          STATUS ││
││ Nov 17, 2026, 1… (◌ Runni…││
││                  27 of 43 …││
││ Nov 16, 2026, 4… (✓ Compl…││
││ 6 non-compliant · 4 insu… ││
│└───────────────────────────┘│
```

(As on Documents, the Status column keeps the whole pill in the build. The date link truncates instead.)

### Inspection page header (every state)

- The back link "Simulated Inspection".
- `h2` "Inspection of {date}" (22/600).
- A meta row: "{regulation} · {establishment}" and the inspection status pill.

The header has no actions.

### Running (Requested or Running), from 768px

The mock's panel as one card:
- the connection notice first, only while live updates are paused or stopped (`mb-5`), as in the Documents cards;
- the stepper strip;
- below it, two columns split by a 1px `--border` divider: the radial progress column (`w-60`, centered) and the theme checklist (`flex-1`);
- the "You can leave this page" caption at the bottom.

```
 ← Simulated Inspection
 Inspection of Nov 17, 2026, 10:42 AM
 Anvisa RDC 216/2004 · Bistro São Paulo   (◌ Running)
 ┌──────────────────────────────────────────────────────────────────────────────┐
 │ ┌ ◌ Live updates paused. Reconnecting…                                     ┐ │ (only while disconnected)
 │ └──────────────────────────────────────────────────────────────────────────┘ │
 │ ┌ (1) Analysis ──────────── (2) Findings ──────────── (3) Action plan ─────┐ │ stepper strip (inset)
 │ └──────────────────────────────────────────────────────────────────────────┘ │
 │        ╭─────╮         │  ● Personal hygiene                        6 of 6  │
 │       │  62%  │        │  ◌ Temperature control                     4 of 7  │
 │        ╰─────╯         │  ◌ Cross-contamination and allergens       2 of 6  │
 │  Inspection in         │  ◌ Cleaning and sanitizing                 3 of 5  │
 │  progress…             │  ○ Pest control                            0 of 4  │
 │  Checking Bistro São   │  ○ Water and premises                      0 of 6  │
 │  Paulo's documents     │  ● Waste                                   3 of 3  │
 │  against Anvisa RDC…   │  ○ Documentation and training              0 of 6  │
 │  27 of 43 requirements │                                                    │
 │  checked               │                                                    │
 │ You can leave this page. The inspection keeps running.                       │
 └──────────────────────────────────────────────────────────────────────────────┘
```

Below 768px the columns stack: the stepper, then the radial centered, then the checklist at full width, then the caption. Below 640px the stepper shows only the current step's label next to the three numbered circles; the other labels stay for screen readers (`sr-only sm:not-sr-only`, with "Done:" kept), so it fits at 320px.

### Failed

The header, then one card with `Message`. No stepper and no findings.

```
 ┌──────────────────────────────────────────────────────────────────────────────┐
 │ ⓘ This inspection stopped before it finished.                                │
 │   It needed more AI processing than one inspection is allowed, so it         │
 │   stopped. No findings were kept. Run it again, and if it stops again,       │
 │   contact your administrator.                                                │
 └──────────────────────────────────────────────────────────────────────────────┘
```

### Completed, from 1280px

The header, then two stacked cards: Findings, then Action plan. The finding detail is a sheet (§5), so the table keeps the full width.

```
 ← Simulated Inspection
 Inspection of Nov 16, 2026, 4:05 PM
 Anvisa RDC 216/2004 · Bistro São Paulo   (✓ Completed)
 ┌ Findings ───────────────────────────────────── [All verdicts ⌄] [All themes ⌄] ┐
 │ 43 findings · 12 pending review                                                │
 │ REQUIREMENT                       THEME          SEVERITY ↓  VERDICT             REVIEW      │
 │ Refrigerated foods are held at    Temperature    Critical    (● Non-compliant)   (◷ Pending) │
 │ 5 °C or below                     control                                                    │
 │ Food handlers wash their hands    Personal       Critical    (◆ Insufficient     (✎ Edited)  │
 │ at the start of work and …        hygiene                       evidence)                    │
 │ The water tank is cleaned every   Water and      Major       (◆ Compliant)       (✓ Accepted)│
 │ six months                        premises                                                   │
 └────────────────────────────────────────────────────────────────────────────────┘
 ┌ Action plan ───────────────────────────────────────────────────────────────────┐
 │ 8 open · 2 done                                                                │
 │ [ ] Record the walk-in temperature twice a day and act on any        (Due in 7 days) │
 │     reading above 5 °C.                                                        │
 │     Refrigerated foods are held at 5 °C or below · Due Nov 23                  │
 │ [ ] Provide records showing: pest control visits every six months  (◆ Overdue)│
 │     Pest control is done by a licensed company · Due Nov 10                    │
 │ [x] Train every food handler on the handwashing SOP and keep signed records.   │
 │     Food handlers wash their hands at the start of work and … · Due Dec 16     │
 └────────────────────────────────────────────────────────────────────────────────┘
```

**768–1279px:** the Theme column is hidden (`hidden xl:table-cell`), and the theme becomes the requirement cell's second line (12px secondary).

### Completed, at 375px

- **Card headers:** the filters wrap below the caption, each at full width (`w-full sm:w-auto`).
- **Findings table:** only the Requirement column is visible. Its cell holds the title (two lines at most), a caption "{severity} · {theme}", then both pills in a row (`aria-hidden`, see §7).
- **Action plan rows:** the badge moves under the caption.

```
┌─────────────────────────────┐
│ [≡] Simulated Inspection    │
│ ← Simulated Inspection      │
│ Inspection of Nov 16, 2026, │
│ 4:05 PM                     │
│ Anvisa RDC 216/2004 · Bist… │
│ (✓ Completed)               │
│┌ Findings ─────────────────┐│
││ 43 findings · 12 pending  ││
││ [ All verdicts         ⌄ ]││
││ [ All themes           ⌄ ]││
││ REQUIREMENT               ││
││ Refrigerated foods are    ││
││ held at 5 °C or below     ││
││ Critical · Temperature c… ││
││ (● Non-compliant) (◷ Pen…)││
│└───────────────────────────┘│
```

### Finding sheet

The finding opens in a right sheet at every width: 480px (`w-full sm:w-120`), full screen below 640px. It has a sticky header and a scrolling body. The sticky footer holds the review actions and shows only with `inspections.review`.

```
                              ┌ Finding 1 of 43                 [˄] [˅] [✕] ┐
                              │──────────────────────────────────────────────│
                              │ Refrigerated foods are held at 5 °C or below │ h2, 15/600
                              │ (● Non-compliant) (◷ Pending)                │
                              │ THEME              SEVERITY     CONFIDENCE   │
                              │ Temperature control Critical    High         │
                              │ Rationale                                    │
                              │ The walk-in log records 9 °C on three        │
                              │ mornings in October, above the 5 °C limit.   │
                              │ Regulation                                   │
                              │ Anvisa RDC 216/2004 · 4.8.15                 │
                              │ Version 2004 · Portuguese                    │
                              │ ┌──────────────────────────────────────────┐ │
                              │ │ ▓conservados sob refrigeração a          │ │ inset, <mark>
                              │ │ ▓temperaturas inferiores a 5ºC▓          │ │
                              │ └──────────────────────────────────────────┘ │
                              │ ▸ Show the whole passage                     │
                              │ Evidence                                     │
                              │ Walk-in temperature log        Open document │
                              │ Version 3 · Portuguese                       │
                              │ ┌ ▓14/10 câmara fria 9 °C▓ ────────────────┐ │
                              │ ▸ Show the whole passage                     │
                              │ Quotes are copies kept with the finding. …   │
                              │ Action                                       │
                              │ Record the walk-in temperature twice a day…  │
                              │ Due Nov 23   (Due in 7 days)                 │
                              │ Review                                       │
                              │ No one has reviewed this finding yet.        │
                              │──────────────────────────────────────────────│
                              │              [✕ Reject] [✎ Edit] [✓ Accept]  │
                              └──────────────────────────────────────────────┘
```

### Reject and Edit dialogs

Centered Form dialogs. Reject is 400px. Edit is 480px and scrolls inside itself on a short screen.

```
┌ Reject finding ─────────────────────┐   ┌ Edit finding ─────────────────────────────┐
│ Reason                              │   │ The original values stay available on the │
│ ┌─────────────────────────────────┐ │   │ finding.                                  │
│ │                                 │ │   │ Verdict              Severity             │
│ │                                 │ │   │ [ Non-compliant  ⌄ ] [ Critical       ⌄ ] │
│ └─────────────────────────────────┘ │   │ Rationale                                 │
│ Say why this finding is wrong. Up   │   │ [ The walk-in log records 9 °C …        ] │
│ to 1,000 characters.                │   │ Action                                    │
│           [Cancel] [Reject finding] │   │ [ Record the walk-in temperature …      ] │
└─────────────────────────────────────┘   │ What the establishment should do, in one… │
                                          │ Due date                                  │
                                          │ [ 2026-11-23                          ▣ ] │
                                          │                  [Cancel] [Save changes]  │
                                          └───────────────────────────────────────────┘
```

Verdict and Severity sit side by side from 640px (`sm:grid-cols-2`) and stack below.

## 3. Components and tokens

| Element | Pattern and tokens |
|---|---|
| Inspections card | The Documents list card. Header `p-5 flex flex-wrap items-start justify-between gap-3`. `h2` (`id="inspections-title"`, `tabIndex={-1}`, `text-title font-semibold`). Caption `text-small text-text-secondary tabular-nums`. |
| Run inspection | **Primary** `Button` with `Play` 16, `w-full sm:w-auto`. While posting: `aria-disabled`, `Spinner surface="mint"`, "Starting…". While unavailable (§4): `aria-disabled` and `aria-describedby` pointing at the notice's body. |
| Run notice | The `Notice` primitive (`mx-5 mb-4`) inside an always-mounted `role="status"` wrapper. Icon per reason (§4). Its action is a link styled with `secondaryButton`: "View progress" or "Go to Documents". |
| Connection notice | Move `ConnectionNotice` from `documents/parts.tsx` to `ui/` with `title` props, and generalize `useLiveStatus` (§8.7). Reconnecting and stopped copy is in §6. It shows on the list and on a Requested or Running inspection only; there it is the running card's first child (`mb-5`), as in the Documents cards. |
| Inspection table | The Documents table classes: `thead bg-inset`, `th scope="col"` (move `th` to `ui/`), rows `relative border-t border-border hover:bg-elevated`, `min-h-14 py-2.5`, a visually hidden `<caption>`. |
| Requested cell | A stretched `Link` (13/500, truncated, inset focus ring on `::after`). Text: the requested date and time (`dateTime()`, §5). Below 768px its second line is the Results text (12px secondary, tabular). |
| Status cell | The status pill, then an optional 11px `text-text-secondary` caption: "{evaluated} of {total} checked" while Running, the short reason when Failed. |
| Results cell | 12px `text-text-secondary tabular-nums`, wrapping: the verdict counts when Completed. Otherwise "—" (`aria-hidden`) with the visually hidden "No results". |
| Status pill (**Proposal**: one primitive) | Generalize `IngestionPill` into `StatusPill({ variant, label })`, keeping the shape `inline-flex h-5 items-center gap-1 rounded-pill border px-2 text-caption font-medium whitespace-nowrap`. The variants are in §4. `IngestionPill` becomes a mapping onto it. |
| Detail header | The Documents detail header: back link with `ArrowLeft` 16, `h2` (`id="inspection-title"`, `text-greeting font-semibold break-words`, `tabIndex={-1}`), then a meta row `mt-1 flex flex-wrap items-center gap-2` with 13px secondary text and the status pill. Below it, cards start `mt-5`. |
| Running card | Card `p-5`. The connection notice (only while disconnected), the stepper strip, then `mt-5 flex flex-col md:flex-row gap-5 md:gap-0`. Radial column `md:w-60 md:pr-5 md:border-r md:border-border flex flex-col items-center text-center`. Checklist `md:pl-5 flex-1`. Caption `mt-5 text-small text-text-secondary`. |
| Stepper (**Proposal**: `ui/Stepper`) | `<ol aria-label="Inspection steps">` on a strip `rounded-sm bg-inset px-3 py-2 flex items-center gap-2`. Each step is a `<li class="flex items-center gap-2">`: a 20px circle (`size-5 rounded-pill`, number `text-caption font-semibold tabular-nums`), then the label (`text-small`). Connectors: `h-px flex-1 min-w-3`. **Done:** `bg-mint` disc with `Check` 12 `text-on-mint`, label `text-text-secondary`, a visually hidden "Done:" first, and a `bg-mint` connector after it. **Current:** `border border-mint` circle with a white number, label `text-text-primary font-semibold`, `aria-current="step"`. **Upcoming:** `bg-elevated` disc, `text-text-secondary` number and label, `bg-neutral` connector. Below 640px only the current step's label shows; the others are `sr-only sm:not-sr-only`. |
| Radial progress (**Proposal**: `ui/RadialProgress`) | An SVG of 120px (`size-30`): a circle with r=55 and stroke 10 in `stroke-chart-track`, and a mint arc with round caps, starting at 12 o'clock, offset by `evaluated / total`. The arc animates with `motion-safe:transition-[stroke-dashoffset] motion-safe:duration-300`. Centered: the percentage, `text-value font-bold tabular-nums` (new token, §8.5), rounded down so 100% shows only once every item is done. The wrapper is `role="progressbar"`, `aria-label` "Requirements checked", `aria-valuemin=0`, `aria-valuemax={total}`, `aria-valuenow={evaluated}`, `aria-valuetext` "{evaluated} of {total} requirements checked". Under it: a title `mt-3 text-body font-semibold`, a body `text-small text-text-secondary`, and the count line `text-small text-text-secondary tabular-nums`. |
| Theme checklist (**Proposal**: `ui/ChecklistRow`) | `<ul aria-label="Themes">`. Rows `flex min-h-9 items-center gap-3`: a 16px glyph, the label `flex-1 text-body text-text-secondary`, and a count `text-small text-text-secondary tabular-nums`. **Done:** a `size-4 rounded-pill bg-mint` disc with `Check` 12 `text-on-mint`. **In progress:** `Spinner` (16px, static under reduced motion). **Not started:** `Circle` 16 `text-neutral`. A visually hidden state prefix (§6). Themes with no items are left out. |
| Failed card | Card `p-5` with the `Message` primitive: `CircleAlert` 20 `text-text-secondary`, `h3` title, body = the long reason. |
| Findings card | Card. Header `p-5 flex flex-wrap items-start justify-between gap-3`: `h2` "Findings" (`id="findings-title"`, `tabIndex={-1}`) with its caption, and on the right `flex flex-wrap gap-2 w-full sm:w-auto` holding the two filters. Then the table, full bleed. |
| Filter select (**Proposal**: `ui/FilterSelect`, DESIGN.md Dropdown) | A native `<select>` with `aria-label`: `h-9 rounded-sm border border-teal-border bg-inset pl-2.5 pr-8 text-small font-medium appearance-none`, and a `ChevronDown` 16 `text-text-secondary` at the right (`pointer-events-none`). `w-full sm:w-auto`. |
| Findings table | The table classes above. **Requirement cell** (`w-full max-w-0`): a stretched `<button>` (`aria-haspopup="dialog"`, 13/500, `line-clamp-2`, text-left), then a 12px secondary line: the theme at 768–1279px, "{severity} · {theme}" below 768px, nothing from 1280px. Below 768px the pills follow in `mt-1.5 flex flex-wrap gap-1.5 md:hidden` with `aria-hidden="true"`. **Theme** `hidden xl:table-cell` 13px secondary. **Severity** `hidden md:table-cell` 13px, Critical `font-semibold text-text-primary`, Major and Minor `text-text-secondary`. **Verdict** and **Review** `hidden md:table-cell`, each holding its pill. |
| Severity sort | The Severity `th` holds a `<button>` "Severity" with `ArrowDown` 14 (critical first) or `ArrowUp` 14, `inline-flex items-center gap-1 rounded-sm hover:text-text-primary`. The `th` carries `aria-sort="descending"` or `"ascending"`. |
| Finding sheet | The `Sheet` primitive, `side="right"`, `className="w-full sm:w-120"`. **Header** `flex items-center gap-1 border-b border-border py-3 pr-3 pl-5`: "Finding {n} of {total}" (`flex-1 text-small text-text-secondary tabular-nums`), then three `iconButton`s: Previous (`ChevronUp` 20), Next (`ChevronDown` 20), Close (`X` 20). **Body** `min-h-0 flex-1 overflow-y-auto px-5 py-4`. **Footer** `border-t border-border px-5 py-4 flex justify-end gap-2`; below 640px each button is `flex-1`. |
| Finding title | `Dialog.Title asChild` on an `h2` (`tabIndex={-1}`, `text-title font-semibold break-words`). Then the pills `mt-2 flex flex-wrap gap-1.5`. |
| Finding facts | `<dl class="mt-4 grid grid-cols-3 gap-3">`: `dt` 12px secondary, `dd` 13px white. Theme, Severity, Confidence. |
| Finding sections | Each is an `h3` (`mt-5 text-small font-semibold text-text-secondary`) followed by its content. The text is 13/20 white with `whitespace-pre-line`. No nested cards: passages are `bg-inset` blocks. |
| Quote block (**Proposal**: extract from `SourcePanel`) | `ui/QuotedPassage({ citation, mode })`. **`quote` mode:** `<blockquote lang={language} class="rounded-sm bg-inset p-3 text-body">` with `text.slice(quoteStart, quoteEnd)` (or `quote` when the offsets don't map) inside the DESIGN.md quote-highlight `<mark>` and its hidden "Quoted passage:" / "End of quoted passage." markers. **`passage` mode:** the SourcePanel's inset passage with the mark. `SourcePanel` then uses `passage` mode. |
| Citation lines | Above each quote. Regulation: "{source} · {section}" 12px secondary, then "Version {version} · {language}" 11px secondary tabular (`languageName`, moved out of `SourcePanel`). Document: the title 13/500 white with "Open document" on the right (mint 12px/500 `Link` to `/documents/{documentId}`, shown with `documents.read`), then "Version {n} · {language}". |
| Whole passage | A native `<details class="mt-2">`. `<summary>` 12px/500 mint, with `ChevronRight` 14 rotating 90° when open (`group-open:rotate-90`, `motion-safe:transition-transform`). The content is the passage-mode block at `mt-2`. |
| Due line | "Due {date}" 13px with `<time dateTime>`, then the Time badge or the Overdue pill (§4). |
| Review section | 13px: the state sentence (§6). A rejection adds a "Reason" label (12px secondary) and the note in a `bg-inset rounded-sm p-3 text-body whitespace-pre-line` block. When `original` differs from the current values: `<details>` "Show the original values" over a `<dl>` with only the changed fields (dt 12px secondary, dd 13px). |
| Review buttons | **Secondary** Reject (`X` 16) and Edit (`Pencil` 16), then **primary** Accept (`Check` 16). While accepting: `aria-disabled`, `Spinner surface="mint"`, "Accepting…". An inline error `role="alert"` (13px, `CircleAlert` 16 secondary) sits above the buttons. |
| Reject dialog | `FormDialog` (400px) holding one textarea "Reason", `maxLength={1000}`, required, with a hint. Submit is the primary "Reject finding" ("Rejecting…"). |
| Edit dialog | `FormDialog` with a new `wide` option, which uses the upload panel's classes (480px, `max-h-[calc(100dvh-2rem)] overflow-y-auto`). A note paragraph (13px secondary), Verdict and Severity (`SelectField`, `sm:grid-cols-2 gap-4`), Rationale and Action (textareas), Due date (`TextField type="date"`). Fields 16px apart. Submit "Save changes" stays `aria-disabled` until a value differs. |
| Textarea field (**Proposal**: `ui/TextAreaField`) | The `TextField` with a `<textarea>`: same label, hint and error, `min-h-22 w-full rounded-control border border-teal-border bg-inset px-3 py-2.5 text-field sm:text-body resize-y`. |
| Action plan card | Card. Header `p-5`: `h2` "Action plan" and a caption. The list is `divide-y divide-border border-t border-border`. |
| Action row | `flex flex-wrap items-start gap-x-3 px-5 py-2`. With `inspections.review`: the `Checkbox` primitive (`flex-1`) labelled with the action text, its description "{finding} · Due {date}". Without it: `ReadOnlyCheckbox` with state "Done:" or "Open:". On the right (`sm:ml-auto`, below 640px under the description with `pl-9`): the Time badge or the Overdue pill. A done row shows no badge. |
| Time badge (**Proposal**: `ui/TimeBadge`) | DESIGN.md Time badge: `inline-flex h-5 items-center rounded-[6px]`. 6px is not a token, so use `rounded-sm` and see §8.5. Then `bg-info-bg px-2 text-caption text-info tabular-nums`, with `<time>`. |
| Loading, empty, error | `Skeleton`, `Message` and `ErrorMessage`, as on Documents. Empty icons are 20px `text-neutral`, error icons 20px `text-text-secondary`. |

## 4. Status rules

**Inspection status pill.** A process state, not compliance, so it reuses the ingestion styles.

| API status | Label | Style (as on Documents) |
|---|---|---|
| `Requested` | Waiting | Waiting: `Clock`, `border-neutral bg-neutral/10 text-text-secondary` |
| `Running` | Running | Processing: 12px `Spinner`, `border-mint bg-mint/10 text-mint` |
| `Completed` | Completed | Indexed: mint `CircleCheck`, `border-teal-border text-text-primary` |
| `Failed` | Failed | Attention: ◆ (`Diamond` 10px filled), `border-warning bg-warning/10 text-warning` |

An unknown status shows like Waiting, with the raw value as its label.

**Verdict pill.** A compliance state, so it uses status colors. Every variant keeps its label and glyph.

| Verdict | Label | Glyph (`aria-hidden`) | Border / fill | Text |
|---|---|---|---|---|
| `compliant` | Compliant | ◆ `Diamond` 10px `fill-current text-success` | `border-success bg-success/10` | `text-success` |
| `non_compliant` | Non-compliant | ● `Circle` 8px `fill-current text-danger` | `border-danger bg-danger/10` | `text-text-primary` (§8.5: `--danger` on its fill is 4.3:1) |
| `insufficient_evidence` | Insufficient evidence | ◆ `Diamond` 10px `fill-current text-warning` (Attention) | `border-warning bg-warning/10` | `text-warning` |

**Review pill.** Not a compliance state, so no green or red.

| Review status | Label | Glyph (`aria-hidden`, 12px) | Border / text | Fill |
|---|---|---|---|---|
| `Pending` | Pending | `Clock` `text-text-secondary` | `border-neutral` / `text-text-secondary` | `bg-neutral/10` |
| `Accepted` | Accepted | `CircleCheck` `text-mint` | `border-teal-border` / `text-text-primary` | transparent |
| `Edited` | Edited | `Pencil` `text-mint` | `border-teal-border` / `text-text-primary` | transparent |
| `Rejected` | Rejected | `CircleX` `text-text-secondary` | `border-teal-border` / `text-text-secondary` | transparent |

**Running view, by state.** `themes` is the per-theme progress of §8.8.

| Inspection | Stepper | Radial title / body | Checklist |
|---|---|---|---|
| `Requested` | 1 current | "Waiting to start…" / "The inspection is queued and starts in a moment." (0%) | hidden until totals arrive |
| `Running`, evaluated < total | 1 current | "Inspection in progress…" / "Checking {establishment}'s documents against {regulation}." | per theme: done when evaluated = total, in progress when 0 < evaluated, else not started |
| `Running`, evaluated = total | 1 done, 2 current | "Preparing findings…" / "Every requirement is checked. Normetry is writing the findings and the action plan." | all done |
| `Completed` | — (the completed view replaces the card) | | |
| `Failed` | — (the failed card) | | |

Step 3 never shows as current. The core sees only Running and Completed, and consolidation plus the action plan take seconds.

**Run inspection availability** (with `inspections.run`). The notice slot shows at most one notice, in this priority:

| Condition | How it is known | Button | Notice (icon · title · body · action) |
|---|---|---|---|
| The list has a `Requested` or `Running` inspection | the list, kept live | `aria-disabled` | `Spinner` · "An inspection is running." · "You can run another one when it finishes." · "View progress" (to it) |
| Quota: 429 with code `inspection_quota` | the POST answer's `Retry-After` | `aria-disabled` until the reset, then enabled and the notice clears | `Clock` · "Your organization has run all of today's inspections." · "You can run another inspection {when}." (`untilReset`, refreshed every minute) |
| Other 429 (rate limit) | `Retry-After` | `aria-disabled` until then | `Clock` · "Too many requests right now." · "Try again {when}." |
| 409 `no_indexed_documents` | the POST answer | enabled | `FileText` · "{establishment} has no indexed documents yet." · "An inspection checks the establishment's own documents. Upload SOPs, logs or reports, and run it once at least one is indexed." · "Go to Documents" (with `documents.read`) |
| 409 `inspection_running` | the POST answer (a stale list) | refetch the list, which then shows the first row | — |
| 403 | the POST answer | refetch `/me` (`useSend`) | `CircleAlert` · "You no longer have permission to do this." |
| Network, 5xx, unknown code | the POST answer | enabled | `CircleAlert` · "Couldn't start the inspection." · "Check your connection and try again." |

A refusal notice clears on the next Run click and when the establishment changes.

**Due dates** (`dueOn` is a date with no time):
- Parse it as a local date (`new Date(y, m - 1, d)`), never with `new Date("2026-11-23")`, which is UTC and shows the day before west of UTC.
- Open, due today or later: the Time badge "Due {when}", with `Intl.RelativeTimeFormat(locale, { numeric: "auto" })` in days ("Due in 7 days", "Due tomorrow", "Due today").
- Open, past due: the Attention status pill "Overdue".
- Done: no badge.

**Action plan contents:** the action items of findings that are not Rejected, sorted by due date, then severity, then finding title. A toggle never reorders the rows.

## 5. Behavior

**Dates.** `dateTime(iso)` in `copy.ts`: `Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" })` gives "Nov 17, 2026, 10:42 AM". It is used for row links, the page title and review times. Two inspections on the same day stay distinct this way.

**History**
- Newest first, as the API sends it.
- A row link opens `/inspections/:id`, and focus moves to the detail `h2`.

**Run**
- A click on "Run inspection" posts.
- **202 `{ id }`:**
  - put a Requested row at the top of the cached list;
  - navigate to `/inspections/{id}`;
  - focus the detail `h2`;
  - announce "Inspection started.";
  - refetch the list.
- **Refusals:** as in §4. The POST goes through `useSend`, extended to return `Retry-After` seconds; reuse the header parser in `chat/answers.ts`.

**Live progress over SignalR** (`/hubs/inspections`, event `InspectionChanged`)
- **Where it lives:** one connection while the screen is open, mounted above both routes, as on Documents. It joins the selected establishment's group, and moves to the new group when the establishment changes.
- **Progress events** (same status, new counts) patch the cached list row and the open inspection: `itemsEvaluated`, `itemsTotal`, `themes`. Nothing is refetched. An event for an id that isn't in the list refetches the list.
- **A status change** refetches the list and the open inspection. When the open inspection turns Completed, its page swaps to the completed view. If focus was inside the running card or on `body`, it moves to the detail `h2`.
- **After every join** (connect, reconnect, Reconnect), refetch both. A missed completion then shows (FR-009).
- **Connection notice:** the Documents rules (2 s grace before "Reconnecting…", "Stopped" at once, Reconnect, focus to the heading after a successful retry). It shows on the list and on a Requested or Running inspection, never on a completed or failed one, where nothing is live.
- **Late completion:** a Failed `timed_out` inspection that later completes swaps to the completed view, and the swap is announced.

**Findings table**
- **Default order:** severity (critical, major, minor), then verdict (non-compliant, insufficient evidence, compliant), then title A–Z.
- **Sort:** the Severity header toggles critical-first and minor-first. The secondary keys stay. Below 768px the header is hidden and the default order holds.
- **Filters** apply on change and combine (AND).
  - Each option shows the count over all findings: "Non-compliant (6)".
  - The theme filter lists only the themes present, in the theme order of §6.
  - Filters and sort reset when another inspection opens. They are not in the URL.
- **Caption:** "{count} findings · {pending} pending review", or "· All reviewed" when no finding is Pending.

**Finding sheet**
- A row's button opens the sheet on that finding.
  - Focus goes to the sheet `h2` (`onOpenAutoFocus` prevented, then focus the heading, as the chat's source sheet does).
- **Previous and Next** walk a snapshot of the table's filtered and sorted order, taken when the sheet opens.
  - A finding edited out of the current filter stays reachable until the sheet closes, and the table re-filters on close.
  - At either end the button is `aria-disabled`.
  - Focus stays on the button. The polite region inside the sheet announces "{title}. Finding {n} of {total}."
- **Closing** (Esc, Close, or the scrim) returns focus to the row of the finding shown last, when it is still in the table. Otherwise focus goes to the Findings `h2`.
- **Quotes** show alone at first. "Show the whole passage" opens the stored passage with the quote highlighted.

**Review** (with `inspections.review`)
- **Accept** posts at once. On success:
  - patch the finding from the response, then refetch the inspection;
  - announce "Finding accepted." in the sheet's region;
  - focus stays on Accept, which is now `aria-disabled` (with `aria-describedby` pointing at the review sentence) while the finding is Accepted.
- **Reject** opens the Reject dialog with focus in Reason, prefilled with the current note when the finding is already Rejected.
  - An empty submit shows "Enter a reason." and keeps focus in the field.
  - On success the dialog closes, focus returns to Reject, and "Finding rejected." is announced. The announcement is set in the dialog's `onCloseAutoFocus`, once the sheet is visible to assistive technology again.
- **Edit** opens the Edit dialog with the current values and focus on Verdict.
  - Only changed fields are sent (`PATCH`).
  - Rules: Rationale is required. Action and Due date go together: both or neither, and neither is allowed only for a finding that has no action.
  - On submit with errors, focus moves to the first invalid field.
  - On success: "Finding updated.", and focus returns to Edit.
- **A later decision replaces an earlier one** (FR-013). Accept after Edit keeps the edited values, and "Show the original values" stays available whenever `original` differs.
- **Errors:** 403 shows "You no longer have permission to do this." and refetches `/me` after the sheet closes (`useSend({ inside: true })`). 404 shows "This finding isn't available anymore." Anything else shows "Couldn't save the review. Try again." A dialog keeps its draft on error.
- **Three clicks or fewer from the list (SC-005):**

| Review | Clicks |
|---|---|
| Accept | row (1) → Accept (2) |
| Reject | row (1) → Reject (2) → type the reason → Reject finding (3) |
| Edit | row (1) → Edit (2) → change fields → Save changes (3) |

**Action plan**
- A checkbox toggle is optimistic: it posts `/done` or `/reopen` at once.
- While its request runs, further clicks on that checkbox are ignored.
- On failure the checkbox reverts, and "Couldn't update the action. Try again." shows under the row (`role="alert"`).
- The checkbox's own state change is announced by screen readers, so success needs no extra message.

## 6. States and copy (EN)

| State | History | Inspection page |
|---|---|---|
| Loading | The card `h2` shows the establishment name, then a count bar (`h-3 w-24`) and 4 skeleton rows (a date bar `h-3 w-2/5` and a pill bar `h-5 w-20`). `aria-busy`, with a visually hidden "Loading inspections…". | Title bar `h-6 w-72` (or the title, when the list has it), meta bar, then one card with a 120px ring skeleton and 8 checklist bars. Visually hidden "Loading inspection…". |
| Empty | `ClipboardCheck` 20 `text-neutral` · "No inspections yet" · with `inspections.run`: "Run an inspection to check {establishment}'s documents against {regulation}. It takes a few minutes."; without: "Inspections of {establishment} appear here." | Completed with no findings: "No findings were kept" / its body. No actions: "No actions to take" / its body. |
| Error | `ErrorMessage`: "Couldn't load inspections" / "Check your connection and try again." / "Try again". | "Couldn't load this inspection", same body and button. |
| Not found (404) | — | "This inspection isn't available" / "It may belong to another establishment, or it no longer exists." and a secondary link "Back to inspections". |
| Refused | No `inspections.read`: "You don't have access to Simulated Inspection". | The same. |
| Filtered to nothing | — | "No findings match these filters" / "Change or clear the filters to see more findings." and a secondary "Clear filters". |

**`copy.ts` additions and changes**, ready to paste. Placeholders are filled with `format()`, and sentences are never concatenated.

```ts
// copy.titles
inspections: "Simulated Inspection · Normetry",
inspection: "Inspection of {date} · Simulated Inspection · Normetry",

// copy.shell
inspections: "Simulated Inspection",

// copy.access (changed)
establishmentsBody:
  "Documents, Simulated Inspection and Ask the Regulation work per establishment. Ask an administrator in your organization for access.",

// copy.areas
inspections: "Simulated Inspection",

// copy.permissions
"inspections.read": {
  label: "View inspections",
  description: "See inspections, their progress, findings and action plans.",
},
"inspections.run": {
  label: "Run inspections",
  description: "Start a simulated inspection of an establishment.",
},
"inspections.review": {
  label: "Review findings",
  description: "Accept, reject or edit findings, and mark actions done.",
},

// new top-level keys
inspections: {
  oneInspection: "1 inspection",
  count: "{count} inspections",
  run: "Run inspection",
  starting: "Starting…",
  loading: "Loading inspections…",
  tableCaption: "Inspections of {establishment}",
  requested: "Requested",
  status: "Status",
  results: "Results",
  resultCounts:
    "{nonCompliant} non-compliant · {insufficient} insufficient evidence · {compliant} compliant",
  checked: "{evaluated} of {total} checked",
  noResults: "No results",
  emptyTitle: "No inspections yet",
  emptyBody:
    "Run an inspection to check {establishment}'s documents against {regulation}. It takes a few minutes.",
  emptyBodyReadOnly: "Inspections of {establishment} appear here.",
  errorTitle: "Couldn't load inspections",
  errorBody: "Check your connection and try again.",
  tryAgain: "Try again",
  reconnecting: "Live updates paused. Reconnecting…",
  stopped: "Live updates stopped. Progress may be out of date.",
  reconnect: "Reconnect",
  /** Pill labels by API status; an unknown status shows as sent. */
  statuses: {
    Requested: "Waiting",
    Running: "Running",
    Completed: "Completed",
    Failed: "Failed",
  } as Record<string, string | undefined>,
  runningTitle: "An inspection is running.",
  runningBody: "You can run another one when it finishes.",
  viewProgress: "View progress",
  noDocumentsTitle: "{establishment} has no indexed documents yet.",
  noDocumentsBody:
    "An inspection checks the establishment's own documents. Upload SOPs, logs or reports, and run it once at least one is indexed.",
  goToDocuments: "Go to Documents",
  quotaTitle: "Your organization has run all of today's inspections.",
  quotaBody: "You can run another inspection {when}.",
  rateLimitedTitle: "Too many requests right now.",
  rateLimitedBody: "Try again {when}.",
  startErrorTitle: "Couldn't start the inspection.",
  startErrorBody: "Check your connection and try again.",
  denied: "You no longer have permission to do this.",
  back: "Simulated Inspection",
  title: "Inspection of {date}",
  meta: "{regulation} · {establishment}",
  loadingInspection: "Loading inspection…",
  loadErrorTitle: "Couldn't load this inspection",
  notFoundTitle: "This inspection isn't available",
  notFoundBody: "It may belong to another establishment, or it no longer exists.",
  backToInspections: "Back to inspections",
  stepsLabel: "Inspection steps",
  steps: ["Analysis", "Findings", "Action plan"],
  stepDone: "Done:",
  waitingTitle: "Waiting to start…",
  waitingBody: "The inspection is queued and starts in a moment.",
  progressTitle: "Inspection in progress…",
  progressBody: "Checking {establishment}'s documents against {regulation}.",
  preparingTitle: "Preparing findings…",
  preparingBody:
    "Every requirement is checked. Normetry is writing the findings and the action plan.",
  progressLabel: "Requirements checked",
  progress: "{evaluated} of {total} requirements checked",
  leaveNote: "You can leave this page. The inspection keeps running.",
  themesLabel: "Themes",
  themeCount: "{evaluated} of {total}",
  themeDone: "Done:",
  themeActive: "In progress:",
  themePending: "Not started:",
  failedTitle: "This inspection stopped before it finished.",
  /** By failure code (the queue contract's reason codes and `timed_out`). */
  failures: {
    budget_exceeded: {
      short: "Processing limit reached",
      long: "It needed more AI processing than one inspection is allowed, so it stopped. No findings were kept. Run it again, and if it stops again, contact your administrator.",
    },
    no_documents: {
      short: "No indexed documents",
      long: "No indexed document of this establishment was found when it started. Upload documents, wait until they're indexed, then run it again.",
    },
    error: {
      short: "Internal error",
      long: "Something went wrong on our side. No findings were kept. Run it again later.",
    },
    timed_out: {
      short: "Timed out",
      long: "It took too long and was stopped. No findings were kept. Run it again.",
    },
  } as Record<string, { short: string; long: string } | undefined>,
  failureUnknown: {
    short: "Couldn't finish",
    long: "It stopped before it finished. No findings were kept. Run it again.",
  },
  started: "Inspection started.",
  milestone: "{evaluated} of {total} requirements checked.",
  completed: "Inspection complete: {findings}.",
  failed: "The inspection stopped: {reason}.",
},
findings: {
  title: "Findings",
  one: "1 finding",
  count: "{count} findings",
  caption: "{findings} · {review}",
  pending: "{count} pending review",
  allReviewed: "All reviewed",
  tableCaption: "Findings of the inspection of {date}",
  requirement: "Requirement",
  theme: "Theme",
  severity: "Severity",
  verdict: "Verdict",
  review: "Review",
  meta: "{severity} · {theme}",
  verdictFilter: "Filter by verdict",
  allVerdicts: "All verdicts",
  themeFilter: "Filter by theme",
  allThemes: "All themes",
  option: "{label} ({count})",
  noMatchTitle: "No findings match these filters",
  noMatchBody: "Change or clear the filters to see more findings.",
  clearFilters: "Clear filters",
  noneTitle: "No findings were kept",
  noneBody:
    "Normetry keeps a finding only when it can quote the regulation, and it couldn't for any requirement in this inspection.",
  verdicts: {
    compliant: "Compliant",
    non_compliant: "Non-compliant",
    insufficient_evidence: "Insufficient evidence",
  } as Record<string, string | undefined>,
  reviews: {
    Pending: "Pending",
    Accepted: "Accepted",
    Rejected: "Rejected",
    Edited: "Edited",
  } as Record<string, string | undefined>,
  severities: { critical: "Critical", major: "Major", minor: "Minor" } as Record<
    string,
    string | undefined
  >,
  confidences: { high: "High", medium: "Medium", low: "Low" } as Record<
    string,
    string | undefined
  >,
  position: "Finding {n} of {total}",
  previous: "Previous finding",
  next: "Next finding",
  close: "Close finding",
  confidence: "Confidence",
  rationale: "Rationale",
  regulation: "Regulation",
  evidence: "Evidence",
  noEvidence: "No passage in this establishment's documents shows this requirement.",
  regulationMeta: "{source} · {section}",
  sourceMeta: "Version {version} · {language}",
  untitledDocument: "Untitled document",
  openDocument: "Open document",
  wholePassage: "Show the whole passage",
  storedCopy:
    "Quotes are copies kept with the finding. The regulation or a document may have changed since.",
  action: "Action",
  due: "Due {date}",
  noAction: "No action needed.",
  reviewHeading: "Review",
  notReviewed: "No one has reviewed this finding yet.",
  acceptedBy: "Accepted by {name} on {date}.",
  rejectedBy: "Rejected by {name} on {date}.",
  editedBy: "Edited by {name} on {date}.",
  reason: "Reason",
  unknownReviewer: "Unknown user",
  original: "Show the original values",
  dueDate: "Due date",
  accept: "Accept",
  accepting: "Accepting…",
  reject: "Reject",
  edit: "Edit",
  reviewError: "Couldn't save the review. Try again.",
  gone: "This finding isn't available anymore.",
  accepted: "Finding accepted.",
  rejected: "Finding rejected.",
  edited: "Finding updated.",
  moved: "{title}. Finding {n} of {total}.",
  rejectTitle: "Reject finding",
  reasonHint: "Say why this finding is wrong. Up to 1,000 characters.",
  reasonRequired: "Enter a reason.",
  rejectSubmit: "Reject finding",
  rejecting: "Rejecting…",
  editTitle: "Edit finding",
  editNote: "The original values stay available on the finding.",
  chooseVerdict: "Choose a verdict",
  chooseSeverity: "Choose a severity",
  actionHint: "What the establishment should do, in one or two sentences.",
  rationaleRequired: "Enter a rationale.",
  actionRequired: "Describe the action.",
  dueRequired: "Choose a due date.",
  save: "Save changes",
  saving: "Saving…",
  cancel: "Cancel",
},
actions: {
  title: "Action plan",
  caption: "{open} open · {done} done",
  meta: "{finding} · Due {date}",
  dueIn: "Due {when}",
  overdue: "Overdue",
  done: "Done:",
  open: "Open:",
  error: "Couldn't update the action. Try again.",
  emptyTitle: "No actions to take",
  emptyBody:
    "Actions appear for non-compliant findings and for findings without enough evidence.",
},
/** Checklist themes in display order; an unknown theme shows as sent. */
themes: {
  personal_hygiene: "Personal hygiene",
  temperature_control: "Temperature control",
  cross_contamination_allergens: "Cross-contamination and allergens",
  cleaning_sanitizing: "Cleaning and sanitizing",
  pest_control: "Pest control",
  water_premises: "Water and premises",
  waste: "Waste",
  documentation_training: "Documentation and training",
} as Record<string, string | undefined>,
```

**Announcements** (polite, the screen's region above both routes):
- "Inspection started." after a Run succeeds.
- `milestone` the first time the open running inspection reaches 25%, 50% and 75%.
- `completed` (with `findings` = `one` or `count`) and `failed` (with the short reason), for any inspection of the establishment.

Inside the sheet, its own polite region announces `accepted`, `rejected`, `edited` and `moved`.

## 7. Accessibility

**Focus order**
- History: Run inspection → the notice's action (View progress or Go to Documents) → Reconnect (if shown) → one tab stop per row.
- Inspection page:
  1. the back link, then Reconnect while running;
  2. when completed: the verdict filter → the theme filter → the Severity sort → one tab stop per finding row;
  3. then the action checkboxes.
- Sheet: the `h2` (initial focus) → Open document and the `<summary>` toggles → Reject → Edit → Accept → wraps to Previous → Next → Close.
- Dialogs: their fields in order → Cancel → submit.

**Focus after navigation**
- To the detail (from a row, or after Run): the detail `h2`.
- Back link: the history `h2`.
- The running card replaced by results: the detail `h2`, if focus was inside the card or on `body`.
- The sheet closing: the row of the finding shown last, else the Findings `h2`.
- Dialogs return focus to Reject or Edit.

**Names and descriptions**
- **History rows:** each date link has `aria-describedby` pointing at its status cell, so it reads "Nov 17, 2026, 10:42 AM, link, Running, 27 of 43 checked".
- **Finding rows:** each button has `aria-describedby` pointing at the Verdict and Review cells, so it reads "Refrigerated foods are held at 5 °C or below, button, Non-compliant, Pending". Hidden cells still describe the row below 768px, which is why the visual pill row there is `aria-hidden`.
- **Tables** keep `th scope="col"` at every width. The sorted column carries `aria-sort`.
- **Icon buttons** carry "Previous finding", "Next finding", "Close finding". Filters carry "Filter by verdict" and "Filter by theme".
- **Stepper:** an `<ol>` with `aria-current="step"`, and "Done:" before done steps.
- **Checklist:** "Done:", "In progress:" or "Not started:" before each theme. Counts are plain text.
- **Radial:** `role="progressbar"` with `aria-valuetext`. It is not a live region.
- **Quotes:**
  - `lang` set to the citation language, on both the quote and the passage;
  - the hidden "Quoted passage:" / "End of quoted passage." markers in `lang={locale}`;
  - `<details>` and `<summary>` are native, so Enter or Space opens them.

**Live regions**
- The screen region carries start, milestones, completion and failure. Progress ticks, pills and table cells are never live.
- The sheet's own region carries the review confirmations and the Previous/Next position. Radix hides everything outside an open dialog, so a region outside would stay silent. A confirmation after a nested dialog is set once that dialog has closed.
- Notices sit in always-mounted wrappers: `role="status"` for the run and connection notices. Inline errors use `role="alert"`.

**Disabled and busy.** Run, Accept, Previous, Next and the dialogs' submits use `aria-disabled`, never `disabled`, so they stay focusable. An unavailable Run points at its reason with `aria-describedby`. Containers are `aria-busy` while loading.

**Status never by color alone.** Every pill has its label and glyph. Severity is a word. Overdue is a labelled pill. Failure reasons are visible text.

**Contrast.**
- The Non-compliant pill text is white (§8.5).
- `--text-muted` appears only on the inset table header.
- `--neutral` appears only as glyphs and connectors.
- All pills keep at least 4.5:1 on `--bg-card` and on the `--bg-elevated` row hover.

**Reduced motion.** The arc jumps to its value. Rings are static. No fade or sheet slide. The `<details>` chevron doesn't animate.

**Touch.** History and finding rows are at least 56px. Fields are 44px. Buttons and icon buttons are 36px. Action rows use the 44px checkbox label row.

## 8. Decisions

1. **Navigation:** "Simulated Inspection" (`ClipboardCheck`) after Documents. Audit Management stays in DESIGN.md's list for later, with another icon.
2. **The finding opens in a right sheet at every width (480px, full screen below 640px), not inline.** It follows the chat's source sheet: the same `Sheet` primitive, edge and animation, a sticky header with Close, stored passages with the quote highlight. Unlike the chat's source panel, it never sits inline at 1280px or wider. The table needs that width for five columns, a finding holds two passages and the review footer, and a modal sheet keeps focus inside while reviewing. Previous/Next make a row-by-row review cheap.
3. **The stepper shows only while running**, as in the mock's panel. The completed page is the Findings card and the Action plan card. The step labels use the app's sentence case ("Action plan"). DESIGN.md writes "Action Plan".
4. **Action plan:** the actions of Rejected findings are hidden, and toggles never reorder rows.
5. **DESIGN.md changes**, landing with the web PR:
   - §3, a new row: `| Progress value | 20px / 700, 28px line height (`--text-value`) | the radial progress percentage; the "/ 133" part of a KPI value |`.
   - §4 Navigation: add "**Simulated Inspection** (Lucide `ClipboardCheck`)" after Documents. "Today the nav reads Ask the Regulation · Documents · Simulated Inspection · Settings."
   - §4, a new bullet: "**Simulated Inspection:** history and inspection pages scroll in main like Documents. A finding opens in a modal sheet from the right at every width, 480px (full screen below 640px), so the findings table keeps the full width."
   - §5 Status pill. Append: "Non-Compliant text is `--text-primary`: `--danger` on its 10% fill is 4.3:1, and 4.2:1 on the `--bg-elevated` row hover. Its border, dot and fill stay `--danger`. Inspection verdicts read Compliant (◆ `--success`), Non-compliant (● `--danger`) and Insufficient evidence (the Attention style)."
   - §5 Ingestion status pill. Append: "An inspection's status reuses the four variants: Waiting (Requested), Running (the Processing style), Completed (the Indexed style), Failed."
   - §5, a new row **Review status pill**: "A finding's review state, not compliance, so no green or red; the Ingestion pill's shape. **Pending**: the Waiting style. **Accepted**: the Indexed style. **Edited**: the Indexed style with a mint `Pencil`. **Rejected**: `--teal-border` border, `--text-secondary` text, transparent fill, `CircleX` in `--text-secondary`."
   - §5 Radial progress. Replace the row with: "A 120px ring with a 10px stroke and round caps: `--chart-track` track, mint arc from 12 o'clock. Centered %: white, `--text-value`, tabular, rounded down. Below it, a 13px/600 white status line and 12px `--text-secondary` lines. `role="progressbar"` with the count as `aria-valuetext`. The arc animates between values (300ms; none under reduced motion)."
   - §5 Stepper. Replace the row with: "An `<ol>` of 20px numbered circles joined by 1px lines on an `--bg-inset` strip (`--radius-sm`, padding 8/12). **Done:** mint disc with an `--on-mint` `Check`, then a mint line. **Current:** 1px mint ring, white number, 12px/600 white label, `aria-current="step"`. **Upcoming:** `--bg-elevated` disc, `--text-secondary` number and label, `--neutral` line. Hidden "Done:" before done labels. Below 640px only the current label shows; the others stay for screen readers."
   - §5 Checklist. Replace the row with: "A 16px glyph, a 13px `--text-secondary` label, and an optional 12px `--text-secondary` tabular count on the right ("4 of 7"). **Done:** mint disc with an `--on-mint` `Check`. **In progress:** the 16px Small ring. **Not started:** `Circle` in `--neutral`. A hidden "Done:", "In progress:" or "Not started:" before the label."
   - §5 Dropdown / select. Append: "36px tall, padding 0/10, 12px/500 white text. A native `<select>`; without a visible label it gets an `aria-label` ("Filter by verdict")."
   - §5 Text field. Append: "A multi-line field (`<textarea>`) has the same border and fill, a minimum height of 88px, padding 10/12, and resizes vertically."
   - §5 Form dialog. Change "max width 400 (480 when it holds an upload)" to "max width 400 (480, scrolling inside itself on a short screen, when it holds an upload or more than three fields)".
   - §5 Time badge. Append: "Its 6px radius is `--radius-sm` in the build (no 6px token). An open action past its due date shows the Attention status pill "Overdue" instead."
   - §8, Simulated Inspection row: "History with Run inspection; stepper, radial progress and checklist by theme while running; findings table with verdict and review pills, a finding sheet with both quotes and Accept / Reject / Edit, and the action plan ([`inspection-screen.md`](inspection-screen.md))".
6. **`index.css`:** add `--success`, `--danger`, `--info`, `--info-bg` and `--chart-track`, which are already in DESIGN.md, with their `--color-*` aliases. Add `--text-value: 20px` and `--text-value--line-height: 28px`.
7. **Reuse before adding:**
   - Generalize `IngestionPill` into `StatusPill`.
   - Move `ConnectionNotice`, `th` and `languageName` to `ui/`.
   - Generalize `documents/live.ts` into one `useLiveStatus(hubUrl, event, establishmentId, onChange, onResync)` that both screens use.
   - Extract `QuotedPassage` from `SourcePanel`.
   - Give `FormDialog` a `wide` option.
   - Extend `useSend` to return `Retry-After`.
   - Reuse `untilReset`, `Notice`, `Checkbox`, `ReadOnlyCheckbox`, `SelectField`, `TextField`, `Sheet`, `Message`, `ErrorMessage` and `Skeleton` as they are.
   - New primitives: `Stepper`, `RadialProgress`, `ChecklistRow`, `FilterSelect`, `TextAreaField`, `TimeBadge`.
8. **Contract needs.** Route these to T007, T008, T010 and T013 before T011 and T014 start.
   1. **Per-theme progress, required for the theme checklist:**
      - `InspectionStarted` and `InspectionProgressed` gain `themes: [{ theme, itemsTotal, itemsEvaluated }]`, a full snapshot, so a redelivered or late event never double counts.
      - The core keeps the snapshot with the highest `itemsEvaluated`, and returns `themes` on `GET .../inspections/{id}` and in `InspectionChanged`.
      - If this is refused, the checklist is dropped and the stepper and radial stay.
   2. **Reviewer names:** findings return `reviewedBy: { id, displayName } | null`, as documents return `uploadedBy`. Otherwise the UI can only show "Unknown user".
   3. **Hub method:** name it `JoinEstablishment`, as on `/hubs/documents` (contracts/api.md says `Join`), so one live hook serves both screens.
   4. **Edit without an action item:** a `PATCH` with `actionText` and `dueOn` on a finding that has no action creates one. If the core won't, the Edit dialog hides Action and Due date for those findings.
   5. **Limits:** rationale at most 2,000 characters and action text at most 1,000. The dialog sets the same `maxLength`.
   6. **`original`:** `{ verdict, severity, rationale, actionText, dueOn }`, or null before the first edit.
   7. **Optional:** a 409 `inspection_not_supported` for establishment types without a checklist (Pharmacy, Clinic). Copy when added: "Inspections aren't available for this establishment yet." / "Normetry has inspection checklists for restaurants only."
9. **Skipped:**
   - A "Pending review" filter (add it if reviewers ask);
   - filters and the open finding kept in the URL;
   - a Cancel button (cancelling is a phase 2 non-goal, even though the mock draws one);
   - live updates of other reviewers' decisions (the hub carries only progress);
   - a requester name on the history (not in the list contract);
   - PT copy (comes with the language switch).
