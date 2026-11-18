# Overview screen

Issue #112, task T010 of `specs/004-phase-2-gaps/tasks.md` (User Story 5, FR-008). This spec follows `docs/design/DESIGN.md` and the mock's Overview grid (`reference.png`): the KPI row, the donut card next to the table card, then the area chart, the upcoming list and the risk card. It reuses the Documents and Simulated Inspection patterns (`documents-screen.md`, `inspection-screen.md`). The repo has no `dataviz` skill (`.claude/skills` or plugin skills), so the charts follow common practice: text equivalents, no color-only meaning, a fixed 0–100% axis, and no chart where a list says it better. Copy is English only until the PT switch; §6 has a short PT glossary for the later switch.

## 1. Navigation and routes

- **Nav item:** "Overview", Lucide `House` (the mock's glyph), linking to `/overview`. It is the **first** item. It shows when the user holds `inspections.read` **or** `documents.read`, and it is active on `/overview`. The nav then reads Overview · Ask the Regulation · Documents · Simulated Inspection · Settings.
- **Route:** `/overview` shows the selected establishment. It is scoped to the establishment like the other screens, and the URL never selects one.
- **Home order.** Today `/` is the chat's new conversation, so Overview cannot come first without moving the chat:
  - `/` → `<Navigate to="/overview" replace />` (static).
  - `/ask` is the chat's new conversation (moved from `/`). `/conversations/:id` doesn't change.
  - At `/overview`, a user holding neither permission is sent to `home` with `<Navigate replace>`, the same pattern the chat uses today.
  - `home` = `/overview` (either permission), else `/ask` (`chat.ask`), else `/settings/profiles` (`profiles.read`). Documents and Simulated Inspection are never home again, because either of their read permissions opens Overview.
  - `canOpenAScreen` doesn't change. The catch-all still goes to `/`.
  - Code that changes for the move: the `App.tsx` routes; `Sidebar` (Ask the Regulation `to: "/ask"`, active on `/ask` or `/conversations/*`); `sectionRoots.chat = "/ask"` and `overview: "/overview"`; `ChatScreen`'s two `navigate("/")` become `/ask`. Tests that start at `/` and expect the chat must start at `/ask`.
- **Top bar `h1`:** "Overview". `<title>`: "Overview · Normetry".
- **Changing establishment** in the switcher navigates to `/overview`. The query key holds the establishment id, so the new one loads with its skeleton (no placeholder data, so the old numbers never show under the new name).
- **Refused states:** with neither permission there is no refusal card, because the route redirects. Without `establishments.read`, main shows the establishments refusal, whose body now names Overview (§6).
- **Permissions.** Overview uses only existing permissions.

| Permission | Shows |
|---|---|
| `inspections.read` | the nav item, "View inspection", the Compliance, Open actions and Findings to review KPIs, Verdicts, Results by theme, Compliance over time, Top risk themes, the empty state |
| `inspections.run` | only the empty state's body, which invites a run |
| `documents.read` | the nav item, the Documents to renew KPI, Expiring documents |

**Sections follow the response, not `/me`.** The API leaves out what the caller cannot read:
- `"latestInspection" in data` decides the inspection sections, and `data.documents !== undefined` the documents sections.
- `/me` decides only the skeleton while loading.
- The query key includes both booleans (`["overview", establishmentId, inspectionsRead, documentsRead]`), so a permission change mid-session refetches without a Try again.

## 2. Layout

Main scrolls, as on Documents. One page header, then three grid rows: `mt-5` after the header, and `mt-4` (16px) between rows and between cards. Breakpoints: 640 (`sm`), 768 (`md`), 1280 (`xl`). The sidebar appears at 1024 and changes nothing here.

### Page header (every state)

- On the left: the `h2` with the **establishment name** (`text-greeting`, 22/600, `id="overview-title"`, `tabIndex={-1}`), then a 13px `--text-secondary` line:
  - "{regulation} · Results from the inspection of {date}", using the latest **completed** inspection's `completedAt`;
  - just "{regulation}" when there is none or the caller lacks `inspections.read`.
- On the right: the **secondary** link "View inspection" (`ClipboardCheck` 16), shown only when a latest inspection exists. It is `w-full sm:w-auto`.
- No primary action (§8.5).

### From 1280px

```
 Overview                                                                       (top bar, 56px)
 Bistro São Paulo                                                         [ ▣ View inspection ]
 Anvisa RDC 216/2004 · Results from the inspection of Nov 16
 ┌ Compliance       [⛨]┐ ┌ Open actions     [☰]┐ ┌ Findings to review [▤]┐ ┌ Documents to renew [◷]┐
 │ 77%                 │ │ 8                   │ │ 12                    │ │ 5                     │
 │ ↑ 8 pts vs previous │ │ (◆ 3 overdue)       │ │ Waiting for a         │ │ (◆ 2 expired) 3 expi- │
 │ inspection          │ │                     │ │ reviewer's decision   │ │ ring in 30 days       │
 └─────────────────────┘ └─────────────────────┘ └───────────────────────┘ └───────────────────────┘
 ┌ Verdicts ──────────────────┐ ┌ Results by theme ──────────────────────────────────────────────┐
 │ Rejected findings aren't   │ │ THEME                  COMPLIANT  NON-COMPLIANT  INSUFFICIENT  COMPLIANCE │
 │ counted.                   │ │                                                 EVIDENCE                  │
 │          ╭─────╮           │ │ Personal hygiene              5              1          0  ▓▓▓▓▓░  83% │
 │         │ 77%  │          │ │ Temperature control           3              3          1  ▓▓▓░░░  42% │
 │         │Compl.│          │ │ Cross-contamination and…      4              0          1  ▓▓▓▓░░  80% │
 │          ╰─────╯           │ │ Cleaning and sanitizing       5              0          0  ▓▓▓▓▓▓ 100% │
 │ ● Compliant      77%  (33) │ │ Pest control                  2              2          1  ▓▓░░░░  40% │
 │ ● Insufficient …  9%   (4) │ │ …                                                                     │
 │ ● Non-compliant  13%   (6) │ │                                                                       │
 └────────────────────────────┘ └───────────────────────────────────────────────────────────────────────┘
 ┌ Compliance over time ───────────────┐ ┌ Expiring documents ──┐ ┌ Top risk themes ──────────────┐
 │ Last 5 completed inspections        │ │ Expired, or expiring │ │ Ranked by non-compliant find- │
 │ 100%                                │ │ in the next 30 days  │ │ ings, weighted by severity.   │
 │  75%                    •────•      │ │──────────────────────│ │───────────────────────────────│
 │  50%   •────•────•─────╱            │ │ Food business licen… │ │ (1) Temperature control       │
 │  25%  ░░░░░░░░░░░░░░░░░░░░░░░░      │ │ License · Nov 10     │ │     3 non-compliant · 1 insu… │
 │   0%                                │ │ (◆ Expired)          │ │ (2) Pest control              │
 │      Jul 3  Aug 20 Sep 30 Oct 28 Nov 16│ │ Pest control certif… │ │     2 non-compliant · 1 insu… │
 │ ▸ Show the data                     │ │ Other · Nov 28       │ │ (3) Personal hygiene          │
 │                                     │ │ [Expires in 10 days] │ │     1 non-compliant · 0 insu… │
 └─────────────────────────────────────┘ └──────────────────────┘ └───────────────────────────────┘
```

- **Row 1:** `dl.grid gap-4 sm:grid-cols-2`, plus `xl:grid-cols-4` with four cards or `xl:grid-cols-3` with fewer.
- **Row 2:** `grid gap-4 xl:grid-cols-3`. Verdicts takes 1 column, Results by theme `xl:col-span-2`.
- **Row 3:** `grid gap-4 md:grid-cols-2 xl:grid-cols-12`, spans as follows:

| Present in row 3 | Compliance over time | Expiring documents | Top risk themes |
|---|---|---|---|
| all three | `md:col-span-2 xl:col-span-5` | `xl:col-span-3` | `xl:col-span-4` |
| no documents section | `md:col-span-2 xl:col-span-7` | — | `md:col-span-2 xl:col-span-5` |
| documents only (no inspection sections, or the empty state) | — | `md:col-span-2 xl:col-span-12` | — |

### 768–1279px

- KPIs: two per row.
- Verdicts: full width, with the donut and legend side by side (container query, §3).
- Results by theme: full width, all five columns.
- Compliance over time: full width.
- Expiring documents and Top risk themes: side by side.

```
 [≡] Overview
 Bistro São Paulo                                              [ ▣ View inspection ]
 Anvisa RDC 216/2004 · Results from the inspection of Nov 16
 ┌ Compliance ────────────────[⛨]┐ ┌ Open actions ───────────────[☰]┐
 └───────────────────────────────┘ └────────────────────────────────┘
 ┌ Findings to review ────────[▤]┐ ┌ Documents to renew ─────────[◷]┐
 └───────────────────────────────┘ └────────────────────────────────┘
 ┌ Verdicts ───────────────────────────────────────────────────────────┐
 │   ╭─────╮     ● Compliant                       77%   (33)           │
 │  │ 77%  │    ● Insufficient evidence             9%    (4)           │
 │   ╰─────╯     ● Non-compliant                   13%    (6)           │
 └─────────────────────────────────────────────────────────────────────┘
 ┌ Results by theme (five columns) ────────────────────────────────────┐
 ┌ Compliance over time (full width) ──────────────────────────────────┐
 ┌ Expiring documents ─────────────┐ ┌ Top risk themes ────────────────┐
```

### At 375px (below 640px)

Everything is one column:
- "View inspection" goes full width under the subtitle.
- KPI cards are one per row.
- The donut stacks above its legend.
- Results by theme keeps **Theme** and **Compliance** only. The theme cell's second line holds the counts (`md:hidden`).
- In the chart, only the first and last x labels show.

```
┌─────────────────────────────┐
│ [≡] Overview                │
│ Bistro São Paulo            │
│ Anvisa RDC 216/2004 · Re-   │
│ sults from the inspection   │
│ of Nov 16                   │
│ [    ▣ View inspection     ]│
│┌ Compliance ────────────[⛨]┐│
││ 77%                       ││
││ ↑ 8 pts vs previous insp… ││
│└───────────────────────────┘│
│┌ Open actions ──────────[☰]┐│
││ 8                         ││
││ (◆ 3 overdue)             ││
│└───────────────────────────┘│
│  … Findings to review, Documents to renew
│┌ Verdicts ─────────────────┐│
││ Rejected findings aren't… ││
││          ╭─────╮          ││
││         │ 77%  │         ││
││          ╰─────╯          ││
││ ● Compliant     77%  (33) ││
││ ● Insufficient…  9%   (4) ││
││ ● Non-compliant 13%   (6) ││
│└───────────────────────────┘│
│┌ Results by theme ─────────┐│
││ THEME          COMPLIANCE ││
││ Personal hyg…  ▓▓▓▓░  83% ││
││ 1 non-compliant · 0 insu- ││
││ fficient evidence · 5 co… ││
│└───────────────────────────┘│
│┌ Compliance over time ─────┐│
││ 100%                      ││
││  …area chart…             ││
││ Jul 3               Nov 16││
││ ▸ Show the data           ││
│└───────────────────────────┘│
│┌ Expiring documents ───────┐│
│┌ Top risk themes ──────────┐│
```

### Empty state (no completed inspection, with `inspections.read`)

Row 1 keeps only the documents KPI, if any. Row 2 is one card in place of the inspection sections. Row 3 holds Expiring documents alone.

```
 Bistro São Paulo
 Anvisa RDC 216/2004
 ┌ Documents to renew ──[◷]┐
 │ 5                       │
 │ (◆ 2 expired) 3 expir…  │
 └─────────────────────────┘
 ┌──────────────────────────────────────────────────────────────────────┐
 │ ☑ No inspection results yet                                          │
 │   Run a simulated inspection to see how Bistro São Paulo complies    │
 │   with Anvisa RDC 216/2004. It takes a few minutes.                  │
 │   [Go to Simulated Inspection]                                       │
 └──────────────────────────────────────────────────────────────────────┘
 ┌ Expiring documents (full width) ─────────────────────────────────────┐
```

## 3. Components and tokens

| Element | Pattern and tokens |
|---|---|
| Page header | `flex flex-wrap items-start justify-between gap-3`. `h2` `text-greeting font-semibold break-words`. Subtitle `text-body text-text-secondary tabular-nums`. "View inspection" is a `Link` with `secondaryButton` and `ClipboardCheck` 16, `w-full sm:w-auto`, `state={{ focusTitle: true }}`. |
| KPI row | `<dl>`. Each card is a `<div>` holding one `<dt>` and two `<dd>`s, which is valid inside a `dl`. |
| KPI card (**new** `ui/KpiCard({ label, Icon, value, children })`) | `flex flex-col rounded-card border border-border bg-card p-4 motion-safe:animate-fade-in`. `<dt class="flex items-start justify-between gap-3 text-body text-text-secondary">` holds the label, then the `IconTile` (`aria-hidden`). Value `<dd class="mt-2 text-kpi font-bold tabular-nums">`. Footer `<dd class="mt-auto flex flex-wrap items-center gap-x-2 gap-y-1 pt-2 text-small text-text-secondary">`. No sparkline. |
| Icon tile (**extract** `ui/IconTile({ Icon })`) | `TypeTile`'s classes as they are: `flex size-7 shrink-0 items-center justify-center rounded-sm border border-border bg-inset`, icon `size-4 text-chart-2`. `TypeTile` then calls it with the type's icon. Icons: Compliance `ShieldCheck`, Open actions `ListTodo`, Findings to review `ClipboardList`, Documents to renew `CalendarClock`. |
| Delta | Visible and `aria-hidden`: `ArrowUp`/`ArrowDown` 14 plus "{points} pts", both `font-medium` in `text-success` or `text-danger` (4.58:1 on `--bg-card`), then "vs previous inspection" in secondary. Then one `sr-only` sentence ("Up 8 points from the previous inspection."). With no change, "Same as the previous inspection" in secondary. |
| Overdue / Expired counts | `StatusPill variant="attention"`, labelled "3 overdue" or "2 expired" (Attention, as DESIGN.md's Overdue and the Documents Expired pills). |
| Section cards | `<section aria-labelledby>`: `rounded-card border border-border bg-card motion-safe:animate-fade-in`. Header `p-5` (`pb-0` before a chart body). `h3` `text-title font-semibold`, caption `text-small text-text-secondary`. Card titles are `h3`, since the page `h2` is the establishment. |
| Verdicts card | `@container` card `p-5`. Body `mt-4 flex flex-col items-center gap-5 @sm:flex-row` (the donut stacks in the 1/3 column at 1280px and sits beside the legend when the card is wide). The legend is `w-full @sm:flex-1`. |
| Donut (**new** `ui/DonutChart({ segments, value, caption })`) | SVG `size-35` (140px), `viewBox="0 0 140 140"`, `-rotate-90` so it starts at 12 o'clock, `aria-hidden`. Segments are circles with r 63, `stroke-width` 14 (DESIGN.md's ~18% of the radius is 13; 14 keeps it on the 2px grid), butt caps, each a `stroke-dasharray` arc offset by the segments before it. A 2-unit gap separates segments when two or more are non-zero (one under the gap draws at 1 unit). Zero segments are left out. Colors: Compliant `stroke-chart-2`, Insufficient evidence `stroke-chart-1`, Non-compliant `stroke-danger` (§8.4). Center, `aria-hidden`: the compliant % `text-value font-bold tabular-nums`, then "Compliant" `text-small text-text-secondary`. |
| Verdict legend | `<ul aria-labelledby="verdicts-title">`, three rows in donut order: Compliant, Insufficient evidence, Non-compliant. Row `flex min-h-9 items-center gap-2`: dot `size-2 shrink-0 rounded-pill` (`bg-chart-2`, `bg-chart-1`, `bg-danger`), label `flex-1 text-body text-text-secondary`, % `text-body font-medium tabular-nums`, then `(33)` `text-small text-text-secondary tabular-nums` (`aria-hidden`) with `sr-only` "33 findings". Zero rows stay, at 0% (0). With proposal §8.3 each row is a `Link` (`-mx-2 rounded-sm px-2 hover:bg-elevated`). |
| Results by theme table | The Documents table, full bleed under the `p-5` header: `thead bg-inset`, `th scope="col"` (`th` from `ui/`), first and last cells `px-5`, rows `border-t border-border h-14 py-2.5`, a visually hidden `<caption>`. Columns: **Theme** (`w-full max-w-0`, 13/500 white, truncated; below 768px a second line `md:hidden` 12px secondary with `copy.inspections.resultCounts`) · **Compliant**, **Non-compliant**, **Insufficient evidence** (`hidden md:table-cell text-right text-body tabular-nums`) · **Compliance** (progress bar, then the %). Rows keep the API's checklist order. With proposal §8.3 the theme is a stretched `Link` (`after:absolute after:inset-0`, inset focus ring, row `relative hover:bg-elevated`) with `aria-describedby` = its Compliance cell. |
| Progress bar (**new** `ui/ProgressBar({ ratio })`) | DESIGN.md Progress bar: `h-1.5 w-16 sm:w-24 rounded-pill bg-chart-track`, with a `bg-chart-1 rounded-pill` fill whose width is `{ratio×100}%` (a data value, so an inline style), and no fill at 0. Always `aria-hidden`: the % next to it (`w-10 text-right text-small font-medium tabular-nums text-text-primary`) carries the value. |
| Compliance over time (**new** `ui/AreaChart({ points, summary })`) | DESIGN.md Area chart, drawn as **inline SVG with no library** (Recharts is not installed; about 60 lines cover a single series). Plot `h-40`, with the y labels on the left (`relative w-9 shrink-0`; 0, 25, 50, 75, 100% at `text-caption text-text-secondary tabular-nums`, right-aligned, `-translate-y-1/2` at their y) and the plot `relative flex-1`. The SVG fills the plot (`absolute inset-0 size-full overflow-visible`, `viewBox="0 0 100 100"`, `preserveAspectRatio="none"`). **Line:** polyline `fill-none stroke-chart-1`, `strokeWidth={2}`, `vectorEffect="non-scaling-stroke"`. **Area:** polygon under the line to y=100, `fill="url(#{useId})"`, a vertical `linearGradient` of `stopColor="var(--chart-1)"` at opacity 0.4 to 0 (DESIGN.md `--chart-area` fades to transparent). **Dots** are HTML, so the stretched SVG can't make them ovals: `absolute size-2 -translate-1/2 rounded-pill bg-chart-1` at `left:{x}%`, `top:{y}%`, each with a native `title` "Nov 16: 77%" (mouse only; the table covers the keyboard). **X** is the inspection order, evenly spaced (`x = i/(n−1)·100`), not a time scale. **X labels** (HTML, `text-caption text-text-secondary tabular-nums`, under the plot): the first (left-aligned) and the last (right-aligned) always; the others (`-translate-x-1/2`) only with 6 points or fewer, from 768px (`hidden md:block`). No gridlines (DESIGN.md). The plot and its labels sit in one `div role="img" aria-label={summary}`. |
| Show the data | A native `<details class="mt-3">`. Its `<summary>` is the finding sheet's: 12px/500 mint, `ChevronRight` 14 with `group-open:rotate-90 motion-safe:transition-transform`. Inside, a table (`mt-2 w-full`, the table classes, header `bg-inset`, visible caption none, `<caption class="sr-only">`): **Inspection** (a `Link` with the date, to `/inspections/{id}`, `state={{ focusTitle: true }}`, 13/500) · **Compliance** (13px tabular, or "—" `aria-hidden` with `sr-only` "No result"). Newest first, every point of `series`, null ones included. |
| Expiring documents | Card, header `p-5` (title, caption, and on the right the secondary link "View all documents" to `/documents` with `focusTitle` when `expired + expiringSoon > list.length`). List `<ul class="divide-y divide-border border-t border-border">`. Row `relative min-h-14 px-5 py-2.5 hover:bg-elevated`. Line 1: the title as a stretched `Link` to `/documents/{id}` (`state={{ focusTitle: true }}`, `block truncate text-body font-medium`, inset focus ring), with `aria-describedby` = the badge. Line 2 `mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-1`: "{type} · {date}" (12px secondary, `<time>`, `typeLabel`, `calendarDate`), then the expiry mark from `documents.ts` `expiry()`: `expired` → `StatusPill attention` "Expired"; `soon` → `TimeBadge` "Expires in 10 days". The badge sits under the title, not on the right, so it fits the 3/12 column. |
| Top risk themes | Card, header `p-5` (title and caption). `<ol class="divide-y divide-border border-t border-border">`. Row `relative flex min-h-14 items-center gap-3 px-5 py-2.5` (`hover:bg-elevated` only when linked). A rank disc `flex size-6 shrink-0 items-center justify-center rounded-pill bg-inset text-small font-semibold tabular-nums`, `aria-hidden` (the `<ol>` numbers it). Then the theme `text-body font-medium` (a stretched `Link` with proposal §8.3) and a line `text-small text-text-secondary tabular-nums` with the counts from `byTheme`. **No bar and no score:** see §8.2. |
| Empty and no-results card | Card `p-5` with `Message` (`h3`): icon 20 `text-neutral`, title, body. Then, in the empty state only, `Link` `secondaryButton mt-4` "Go to Simulated Inspection" to `/inspections` with `focusTitle`. |
| Card messages | `Message` inside the card body (`mt-4`), icon 20 `text-neutral`: no trend `ChartLine`, no documents `FileText`, no risk `ShieldCheck`. |
| Loading | §6. Skeleton bars via `Skeleton`. The donut ring is `block size-35 rounded-pill border-14 border-elevated motion-safe:animate-pulse`, as the inspection page's ring. |
| Error | Header (name known), then a card `p-5` with `ErrorMessage`. |

## 4. Data rules

**Numbers**
- **Percentages are rounded down**, as the radial progress does, so 100% shows only when everything is compliant. For counts, compute with integers first: `Math.floor(compliant * 100 / total)`. For the API's ratios (`compliance`, `series[].compliance`) use `Math.floor(ratio * 100 + 1e-9)`: `0.29 * 100` is `28.999…`, and flooring it gives 28.
  - Add `percent(ratio)` to `copy.ts`, which formats with `Intl.NumberFormat(locale, { style: "percent" })` after that floor.
  - The KPI, the donut center and the Compliant legend row use `verdictCounts` (the integers), which by the API's definition equal `compliance`.
- **Compliance** = compliant ÷ (compliant + non-compliant + insufficient evidence). Rejected findings are left out (API). It is null when no finding is left: the KPI shows "—" (`aria-hidden`) with `sr-only` "No result" and an empty footer.
- **Delta:**
  - latest = the last point of `series` (oldest first; it is `latestInspection`);
  - previous = the nearest earlier point whose compliance isn't null;
  - points = the latest's displayed % minus the previous one's, so the delta matches what's on screen;
  - no previous point, or a null latest: "No earlier inspection to compare" (or an empty footer when the latest is null).
- **Open actions, Findings to review:** `openActionItems`, `overdueActionItems` and `pendingReviews` belong to the latest inspection (API).
  - Footers: overdue > 0 → the pill "{n} overdue", else "None overdue". Pending > 0 → "Waiting for a reviewer's decision", else "All findings reviewed".
- **Documents to renew:**
  - Value: `expired + expiringSoon`.
  - Footer: the pill "{expired} expired" when > 0, then "{expiringSoon} expiring in 30 days" when > 0. Both 0: "Nothing expires in the next 30 days".
- **Theme compliance:** per row, compliant ÷ the row's total, by the integer rule.
- **Top risk themes:** the API's order (up to 3, score > 0). Each row's counts come from the `byTheme` row of the same theme. The score itself is never shown.

**Sections by state** (`I` = the inspection sections are in the response, `D` = documents)

| State | Row 1 | Row 2 | Row 3 |
|---|---|---|---|
| `I`, latest null | documents KPI (`D`) | empty card | Expiring documents (`D`) |
| `I`, latest with compliance null | Compliance "—", Open actions, Findings to review, documents KPI (`D`) | no-results card | Compliance over time, Expiring documents (`D`) |
| `I`, latest with results | four (three without `D`) | Verdicts, Results by theme | Compliance over time, Expiring documents (`D`), Top risk themes |
| `D` only | documents KPI | — | Expiring documents |

- **Compliance over time** needs **2 or more points with a result**. Otherwise its card shows the "No trend yet" message, with no chart and no table.
- **Points with a null compliance** are left out of the line and dots. They stay in the data table, as "—".
- **Expiry dates:** `expired` and `expiringSoon` count today in UTC (API), while the row's mark uses the browser's day (`expiry()`). Near midnight they can disagree by a day, as the Documents spec accepts.

## 5. Behavior

- **Data:** `useOverview(establishmentId, inspectionsRead, documentsRead)` in `api/queries.ts`, `GET /establishments/{e}/overview`, with the key from §1. It is enabled only while the screen shows (AppShell renders it only when allowed and an establishment is selected).
  - Types: `Overview { latestInspection?: LatestInspection | null; openActionItems?; overdueActionItems?; pendingReviews?; series?: CompliancePoint[]; documents?: { expired; expiringSoon; list: ExpiringDocument[] } }`.
- **Freshness:** no live connection. It refetches on mount (`staleTime` 0), so returning from a review or an upload shows current numbers. Window focus doesn't refetch (the client default).
- **Links** (all to the selected establishment's own pages):
  - "View inspection" and the data table's dates go to `/inspections/{id}`.
  - Document rows go to `/documents/{id}`; "View all documents" to `/documents`; "Go to Simulated Inspection" to `/inspections`.
  - All of them carry `state={{ focusTitle: true }}`.
  - With proposal §8.3: legend rows go to `/inspections/{id}?verdict={code}`, and theme and risk rows to `?theme={code}`.
- **Arrival focus on Simulated Inspection (required change).** `InspectionsScreen` gains the Documents arrival effect (`location.state.focusTitle` keyed on `location.key`). It focuses:
  - the history `h2`;
  - or, on the detail, the inspection `h2` once it is in the DOM (the title is a skeleton until the detail loads when the list isn't cached);
  - or, with §8.3, the Findings `h2` when the URL carried a filter.
- **Focus on this screen:**
  - A new `focusKey` (an establishment chosen in the drawer, or a retry that loaded the list) focuses `overview-title`.
  - A successful Try again after a load error also focuses it, since the button goes away.
  - A nav click keeps the default (focus stays on the link; the drawer closes as today).
- **Error:** any failure shows the error card. A 403 also refetches `/me` (the query cache's `onError`); the key then changes with the permissions, or the route redirects.

## 6. States and copy (EN)

| State | Page |
|---|---|
| Loading | The header `h2` (establishment name) and a subtitle bar `h-3 w-64`. Then, per `/me` permissions: KPI skeleton cards (a label bar `h-3 w-24`, a value bar `h-7 w-16`, a footer bar `h-3 w-32`). With `inspections.read`: row 2 with the ring skeleton and 5 table row bars, and row 3 with a `h-40` block and 3 row bars. With `documents.read`: 3 row bars in the documents card. The wrapper is `aria-busy="true"` with `sr-only` "Loading overview…". |
| Error | `ErrorMessage` "Couldn't load the overview" / "Check your connection and try again." / "Try again". |
| Empty (no completed inspection) | `ClipboardCheck` · "No inspection results yet" · with `inspections.run`: "Run a simulated inspection to see how {establishment} complies with {regulation}. It takes a few minutes."; without it: "Results show here once an inspection of {establishment} completes." · "Go to Simulated Inspection". |
| No results (compliance null) | `SearchX` · "No results in the latest inspection" / "Every finding of the inspection of {date} was rejected, or none was kept." ("View inspection" in the header is the way on.) |
| No trend | `ChartLine` · "No trend yet" / "Compliance over time shows once two inspections have results." |
| No documents to renew | `FileText` · "No documents to renew" / "No document of {establishment} has expired or expires in the next 30 days." |
| No risk themes | `ShieldCheck` · "No themes at risk" / "No finding of the latest inspection is non-compliant or lacks evidence." |
| Refused | Neither permission: redirect to `home` (§1). Without `establishments.read`: the establishments refusal. |

**`copy.ts` additions and changes**, ready to paste. Placeholders are filled with `format()`, never concatenated. Verdict and theme labels reuse `copy.findings.verdicts`, `copy.findings.theme` and `copy.themes`; document types use `copy.documents.types`; the expiry marks use `copy.documents.expired`/`expiresIn`; the counts line below 768px uses `copy.inspections.resultCounts`.

```ts
// copy.titles
overview: "Overview · Normetry",

// copy.shell
overview: "Overview",

// copy.access (changed)
establishmentsBody:
  "Overview, Documents, Simulated Inspection and Ask the Regulation work per establishment. Ask an administrator in your organization for access.",

// new top-level key
overview: {
  results: "{regulation} · Results from the inspection of {date}",
  viewInspection: "View inspection",
  loading: "Loading overview…",
  errorTitle: "Couldn't load the overview",
  errorBody: "Check your connection and try again.",
  tryAgain: "Try again",
  compliance: "Compliance",
  noResult: "No result",
  deltaPoints: "{points} pts",
  deltaVs: "vs previous inspection",
  deltaUp: "Up {points} points from the previous inspection.",
  deltaDown: "Down {points} points from the previous inspection.",
  deltaSame: "Same as the previous inspection",
  noPrevious: "No earlier inspection to compare",
  openActions: "Open actions",
  overdue: "{count} overdue",
  noneOverdue: "None overdue",
  toReview: "Findings to review",
  waitingReview: "Waiting for a reviewer's decision",
  allReviewed: "All findings reviewed",
  toRenew: "Documents to renew",
  expired: "{count} expired",
  expiring: "{count} expiring in 30 days",
  nothingToRenew: "Nothing expires in the next 30 days",
  verdicts: "Verdicts",
  verdictsCaption: "Rejected findings aren't counted.",
  oneFinding: "1 finding",
  findings: "{count} findings",
  byTheme: "Results by theme",
  byThemeTable: "Results by theme of the inspection of {date}",
  complianceColumn: "Compliance",
  trend: "Compliance over time",
  trendCaption: "Last {count} completed inspections",
  trendSummary:
    "Compliance across the last {count} completed inspections, from {first} on {firstDate} to {last} on {lastDate}.",
  point: "{date}: {value}",
  showData: "Show the data",
  trendTable: "Compliance by inspection",
  inspection: "Inspection",
  noTrendTitle: "No trend yet",
  noTrendBody: "Compliance over time shows once two inspections have results.",
  documents: "Expiring documents",
  documentsCaption: "Expired, or expiring in the next 30 days",
  documentMeta: "{type} · {date}",
  viewAllDocuments: "View all documents",
  noDocumentsTitle: "No documents to renew",
  noDocumentsBody: "No document of {establishment} has expired or expires in the next 30 days.",
  risk: "Top risk themes",
  riskCaption: "Ranked by non-compliant findings, weighted by severity.",
  riskCounts: "{nonCompliant} non-compliant · {insufficient} insufficient evidence",
  noRiskTitle: "No themes at risk",
  noRiskBody: "No finding of the latest inspection is non-compliant or lacks evidence.",
  emptyTitle: "No inspection results yet",
  emptyBody:
    "Run a simulated inspection to see how {establishment} complies with {regulation}. It takes a few minutes.",
  emptyBodyReadOnly: "Results show here once an inspection of {establishment} completes.",
  goToInspections: "Go to Simulated Inspection",
  noResultsTitle: "No results in the latest inspection",
  noResultsBody: "Every finding of the inspection of {date} was rejected, or none was kept.",
},
```

**Formatters (`copy.ts`):**
- `percent(ratio)`: §4.
- `shortDay(iso, now = new Date())`: "Nov 16" this year, "Nov 16, 2025" in another, from a timestamp. It is the date branch of `shortDate`, without its time-of-day case, which would put "4:05 PM" on an axis. Used for the subtitle, the axis, the dot titles, the summary and the data table.
  - The subtitle uses the day, not `dateTime`. The overview has only `completedAt`, while the inspection page's title uses `requestedAt`, and two different times for one inspection would confuse.

**PT glossary, for the language switch:** Overview → Visão geral · Compliance → Conformidade · Open actions → Ações abertas · Findings to review → Achados para revisar · Documents to renew → Documentos a renovar · Verdicts → Veredictos · Results by theme → Resultados por tema · Compliance over time → Conformidade ao longo do tempo · Expiring documents → Documentos a vencer · Top risk themes → Temas de maior risco · View inspection → Ver inspeção · Go to Simulated Inspection → Ir para Inspeção simulada.

## 7. Accessibility

**Focus order** (DOM order = visual order at every width)
1. "View inspection".
2. With §8.3, the legend links.
3. Results by theme rows (one stop per row, §8.3).
4. "Show the data" (`<summary>`), then the table's date links when it is open.
5. Document rows, then "View all documents".
6. Risk theme rows (§8.3).

The KPI row has no stops. Without §8.3, items 2, 3 and 6 are plain text.

**Headings:** `h1` Overview (top bar) → `h2` the establishment → an `h3` per card, and `Message` titles as `h3`. KPI labels are `<dt>`, so they don't crowd the heading list.

**Charts have text equivalents.**
- **Donut:** the SVG and the center are `aria-hidden`. The legend list is the equivalent ("Compliant, 77%, 33 findings").
- **Area chart:** a `role="img"` wrapper whose `aria-label` is the summary sentence. "Show the data" gives every value as a table with links. The `title` tooltips on the dots are a mouse convenience only.
- **Progress bars:** `aria-hidden`; the % sits in the cell.
- **Risk:** an `<ol>`, so the rank is read; the disc is `aria-hidden`.

**Tables:** `th scope="col"` at every width, and a visually hidden `<caption>`. Count cells hidden below 768px are `display:none`, so the theme cell's visible second line carries the counts there. A theme row link's `aria-describedby` points at its Compliance cell ("Temperature control, link, 42%").

**Names:**
- Document links are described by their mark: "Food business license, link, Expired".
- Every link text says where it goes; there are no bare "View all →" links.
- The delta has a full `sr-only` sentence, and its arrow fragments are `aria-hidden`.

**Live regions: none.** The overview isn't live and announces nothing. Loading is `aria-busy` with a hidden label. Error cards are `role="alert"` (`ErrorMessage`). The shell's existing region keeps announcing an establishment choice.

**Status never by color alone:**
- The overdue and expired counts are labelled pills with the ◆ glyph.
- Verdict shares are labelled legend rows.
- Deltas carry an arrow and a sentence.
- Chart color pairs are below 3:1 (`--chart-1` vs `--chart-2` is about 1.7:1), so segments have 2px gaps and the legend carries the meaning.

**Contrast:**
- `--text-muted` appears only in the inset table headers.
- `--neutral` appears only as icons.
- The line and dots (`--chart-1`) reach 4.7:1 against `--bg-card`, and the red segment 4.6:1.
- The delta in `--danger` is 4.58:1 at 12px/500 on `--bg-card`, which passes.

**Touch:** table, document and risk rows are 56px or taller; legend rows 36px; buttons 36px.

**Reduced motion:** no card fade and no `<details>` chevron turn. Nothing else moves (the line doesn't draw, §8.10).

## 8. Decisions

1. **Chat moves to `/ask`** (needed for "Overview first"; §1). Old `/` bookmarks now open Overview. The cost is test churn in `ChatScreen` and `App` tests. *Needs the user's OK.*
2. **Top risk themes: a ranked list, not the mock's second donut** (*proposal*).
   - A donut implies parts of a whole. The API sends only the top 3, and the score (severity-weighted, no unit) isn't a share, so "32%" would be invented.
   - A bar per theme would sit next to the compliance bars of Results by theme with the opposite meaning: higher is worse there, better here.
   - So the list shows each theme's rank, its name and its verdict counts, and the caption explains the ranking.
3. **Filters from the URL on the inspection page** (*proposal*; it gives "links into the findings").
   - `FindingsCard` reads `?verdict=` and `?theme=` once when it mounts and validates them: a code that isn't among the options is ignored. Then it drops them from the URL with `replace`. Afterwards filters stay out of the URL, as `inspection-screen.md` §5 says.
   - **Fallback if refused:** the legend, theme and risk rows are plain text, and "View inspection" is the only way into the findings.
4. **Verdict colors in charts** (*proposal*; DESIGN.md has no mapping): Compliant `--chart-2` (the donut's main segment), Insufficient evidence `--chart-1`, Non-compliant `--chart-danger`. Data is blue and red is only for the non-compliant share, so amber (the pill's Attention style) never appears in a chart.
5. **Page header:** the `h2` is the establishment name, not the mock's "Welcome back, Jane", because the Overview is per establishment and the heading is the focus target. There is no primary action. "Run inspection" here would duplicate the history's availability and refusal notices; the empty state links there instead. "Export Report" has no feature behind it.
6. **KPI cards:** four (Compliance, Open actions, Findings to review, Documents to renew), not the mock's five. No sparklines (Compliance over time sits one row below), and the cards are not links.
7. **Charts are inline SVG.** No dependency: Recharts is not installed and one area chart plus one donut don't justify it. Revisit when a chart needs interaction such as tooltips or zoom.
8. **DESIGN.md changes**, landing with the web PR:
   - §3, replace the KPI value row with: `| KPI value | 28px / 700, 36px line height (`--text-kpi`) | "87%", "114 / 133" (the "/ 133" part is 20px / 500) |`.
   - §4 Navigation: write "Overview (Lucide `House`)" in the item list, and change the last sentence to "Today the nav reads Overview · Ask the Regulation · Documents · Simulated Inspection · Settings."
   - §4, replace the **Overview grid** bullet with: "**Overview grid:** 1. KPI row: four equal cards (Compliance, Open actions, Findings to review, Documents to renew), three per row when a permission hides some; two per row below 1280px, one below 640px. 2. Verdicts donut (1/3) + results by theme table (2/3); stacked below 1280px. 3. Compliance over time (5/12) + expiring documents (3/12) + top risk themes (4/12); below 1280px the chart takes the full width and the other two share a row, one column below 768px."
   - §5 KPI card, append: "The row is a `<dl>`: the label (with the tile) is the `<dt>`, the value and the footer are `<dd>`s. The footer holds a delta, a status pill with a caption ("3 overdue", "2 expired"), or a caption. The icon tile is the 28px `--bg-inset` tile with a 1px `--border` and a `--chart-2` icon."
   - §5 Donut, append: "Segments start at 12 o'clock with 2px gaps between them. Inspection verdicts: Compliant `--chart-2`, Insufficient evidence `--chart-1`, Non-compliant `--chart-danger`. The ring and its center are decorative; the legend list is the text equivalent. Below a 384px-wide card the legend goes under the ring."
   - §5 Area chart, append: "Inline SVG. X is the order of the points, evenly spaced, labelled with dates: the first and the last always, all of them from 768px when there are six or fewer. 8px dots. The chart is a `role="img"` with a summary sentence, followed by a "Show the data" disclosure holding the values as a table."
   - §5, a new row **Ranked list**: "An `<ol>` of rows at least 56px tall, divided by 1px `--border`: a 24px `--bg-inset` disc with the rank (12px/600, tabular, hidden from screen readers; the list numbers it), the name 13px/500 white and a 12px `--text-secondary` line. No bar when the score has no unit."
   - §7, append: "Charts are inline SVG until one needs interaction that a few lines can't give; then Recharts."
   - §8, Overview row: "KPI row, verdicts donut, results by theme table, compliance over time, expiring documents, top risk themes ([`overview-screen.md`](overview-screen.md))".
9. **`index.css`:**
   - add `--chart-1: #0084ed` with `--color-chart-1`;
   - add `--text-kpi: 28px` and `--text-kpi--line-height: 36px`.
   - Non-compliant segments and dots use the existing `danger` (DESIGN.md's `--chart-danger` has the same value; no duplicate token).
10. **Reuse before adding:**
    - **Reused:** `StatusPill`, `TimeBadge`, `Message`, `ErrorMessage`, `Skeleton`, `secondaryButton`, `th`, the `<details>` disclosure, `expiry()`, `typeLabel`, `verdictLabel`, `themeLabel`, `regulationOf`, `calendarDate`, `queryFailed`.
    - **Extracted:** `IconTile` from `TypeTile`.
    - **New primitives in `ui/`:** `KpiCard`, `ProgressBar`, `DonutChart`, `AreaChart`.
    - The expiring-documents and risk lists are screen markup from existing tokens.
    - **New in `overview/`:** `OverviewScreen.tsx`, `overview.ts` (delta, theme ratios, plotted points, row 3 spans) and their tests.
    - `src/web/CLAUDE.md` needs: the `overview/` folder, `/ask`, the new home order, the new primitives, and "Recharts: not added; charts are inline SVG".
11. **Contract needs:** none. The overview response covers every section.
12. **Skipped:**
    - live updates over SignalR (add them if people keep Overview open while inspections run);
    - the period dropdown ("6 Months ⌄"; the API sends the last 12);
    - the line-draw animation;
    - sparklines;
    - hover tooltips beyond native `title`;
    - Export Report;
    - PT copy (comes with the language switch).

## 9. Tests (each state with `expectNoAxeViolations()`)

- `/` lands on `/overview` for a user with either permission. A chat-only user lands on `/ask`, a Settings-only user on `/settings/profiles`. Overview is first in the nav, with `aria-current` on `/overview`.
- Full data: the KPI values, the floor rounding (0.29 → 29%), the delta up, down and same, the overdue and expired pills, the legend shares, the theme rows, the chart summary, and the data table (null shown as "No result").
- `inspections.read` only: no documents KPI or card, and row 3 spans 7/5. `documents.read` only: the documents KPI and card only, with no "View inspection".
- No completed inspection: with and without `inspections.run`, and "Go to Simulated Inspection" focuses the history `h2`. Compliance null: the no-results card and the "—" KPI. One plotted point: "No trend yet".
- Loading (`aria-busy`). Error, then Try again focuses `overview-title`. Changing establishment shows the skeleton, never the old numbers.
- Document row links focus the document `h2`. "View all documents" shows only when the list is cut.
- At 375 (`mockViewport`): the counts line under the theme, and the first and last x labels only.
- With §8.3: `?theme=` and `?verdict=` preset the filters, an unknown code is ignored, and focus lands on the Findings `h2`.
