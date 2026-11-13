# Normetry Design System

![Approved reference](reference.png)

`reference.png` is the approved mock and the source of truth for look and feel. This file turns it into exact values and rules. When they disagree, the image wins on appearance and this file wins on numbers — then fix this file.

> **Content in the mock is placeholder.** ISO 27001 controls, "Acme Corporation", "Jane Cooper" and the dates only illustrate layout. Normetry's real domain is food-safety regulation (FDA Food Code, EU Regulation 852/2004, Anvisa RDC 216/2004): keep the layout and style, swap the content.

## 1. Brand

- **Logo:** geometric "N" mark with a mint → teal gradient, followed by the wordmark "Normetry" in white Inter semibold. Mark alone is used in compact places (chat avatar, panel headers).
- **Tagline:** "Understand Regulations. Stay Ahead."
- **Mood:** dark, calm, precise. Deep navy surfaces, **one** mint accent for action, blues for data, status colors only for status.
<!-- TODO(fill later): export the logo as SVG to docs/design/logo.svg; today it exists only inside reference.png -->

## 2. Color tokens

Values sampled from the rendered mock. The mock's palette card labels slightly different hexes (Teal `#103846`, Navy `#172B3A`, Mint `#35B6A1`, Slate `#F4F7FB`); the UI itself is drawn with the values below, so these are canonical.

### Surfaces

| Token | Hex | Use |
|---|---|---|
| `--bg-canvas` | `#010A18` | page background, behind the app frame (subtle radial glow `--canvas-glow` toward the bottom right) |
| `--canvas-glow` | `#001427` | the radial glow on `--bg-canvas` (auth screens) |
| `--bg-sidebar` | `#010D1B` | sidebar |
| `--bg-app` | `#04192E` | main content area |
| `--bg-card` | `#06182E` | cards |
| `--bg-inset` | `#041425` | table body, inner panels, inputs |
| `--bg-elevated` | `#0C203B` | AI Assistant card, assistant chat bubble, dropdowns, popovers, dialogs |
| `--bubble-user` | `#024265` | user chat bubble (white text, 10.6:1), sampled from the mock |
| `--border` | `#0E233C` | 1px borders of cards, inputs, dividers |
| `--scrim` | `--bg-canvas` at 70% (`rgb(1 10 24 / 0.7)`) | behind a modal drawer or dialog |

### Brand and text

| Token | Hex | Use |
|---|---|---|
| `--mint` | `#0DC6CE` | primary button, send button, radial progress, links ("View source →"), focus ring, active step |
| `--mint-hover` | `#09B9B8` | hover / pressed of mint elements |
| `--on-mint` | `#06283A` | text and icons on mint (dark, never white) |
| `--teal` | `#005E7A` | active sidebar item background |
| `--teal-border` | `#2E7D99` | secondary button and outlined action borders |
| `--teal-deep` | `#064C5E` | "NEW" badge background and open citation chip (mint text, 4.5:1); quote highlight (white text) |
| `--text-primary` | `#FFFFFF` | titles, KPI values, table primary text |
| `--text-secondary` | `#A9B8CC` | subtitles, card labels, legend labels |
| `--text-muted` | `#6B7F99` | table headers on `--bg-inset`, captions and timestamps on the sidebar; only on `--bg-sidebar` and `--bg-inset` |

### Status (only for compliance state and deltas)

| Token | Hex | Use |
|---|---|---|
| `--success` | `#03DA96` | Compliant, positive delta (↑) |
| `--warning` | `#F2AE13` | Partially Compliant, Attention |
| `--danger` | `#ED3F45` | Non-Compliant, negative delta (↓) |
| `--neutral` | `#5B6B82` | Not Applicable, empty-state icons; graphics only, never text |
| `--alert` | `#F4C33A` | "!" badge on change-monitor documents |
| `--info` / `--info-bg` | `#0DECFF` / `#06315E` | time badges such as "In 14 days" |

**Contrast rule.** `--text-muted` reaches 4.5:1 only on the darkest surfaces, so it is used on `--bg-sidebar` and `--bg-inset` and nowhere else; on `--bg-app`, `--bg-card` and `--bg-elevated` use `--text-secondary`. `--neutral` is for graphics (icons, chart segments, status glyphs), never for text.

### Data visualization

| Token | Hex | Use |
|---|---|---|
| `--chart-1` | `#0084ED` | progress bars, area-chart line |
| `--chart-2` | `#23B6FD` | donut main segment, sparklines |
| `--chart-area` | `#0084ED` → `#012649` | area fill: vertical gradient to transparent |
| `--chart-danger` | `#ED3F45` | non-compliant segment, negative sparkline |
| `--chart-neutral` | `#5B6B82` | N/A segment, progress track uses `#0C1E36` |

```css
:root {
  --bg-canvas: #010a18; --bg-sidebar: #010d1b; --bg-app: #04192e; --bg-card: #06182e;
  --bg-inset: #041425; --bg-elevated: #0c203b; --border: #0e233c; --bubble-user: #024265;
  --canvas-glow: #001427; --scrim: rgb(1 10 24 / 0.7);
  --mint: #0dc6ce; --mint-hover: #09b9b8; --on-mint: #06283a;
  --teal: #005e7a; --teal-border: #2e7d99; --teal-deep: #064c5e;
  --text-primary: #ffffff; --text-secondary: #a9b8cc; --text-muted: #6b7f99;
  --success: #03da96; --warning: #f2ae13; --danger: #ed3f45; --neutral: #5b6b82;
  --alert: #f4c33a; --info: #0decff; --info-bg: #06315e;
  --chart-1: #0084ed; --chart-2: #23b6fd; --chart-track: #0c1e36;
  --radius-card: 12px; --radius-control: 10px; --radius-sm: 8px; --radius-pill: 9999px;
}
```

shadcn/ui mapping: `--background` = `--bg-app`, `--card` = `--bg-card`, `--popover` = `--bg-elevated`, `--primary` = `--mint`, `--primary-foreground` = `--on-mint`, `--border` / `--input` = `--border`, `--ring` = `--mint`, `--muted-foreground` = `--text-secondary`. No `--destructive`: red is for status only, so there are no red buttons. A destructive action is confirmed in an alert dialog whose primary button names the verb ("Delete"). Dark theme only for now. `--text-muted` has no shadcn alias: components use it explicitly (`text-text-muted`), and only on `--bg-sidebar` and `--bg-inset`.

**Hover.** There is no `--bg-hover` token and no separate hover color: nav items, list rows and buttons on `--bg-sidebar`, `--bg-app` or `--bg-card` hover to `--bg-elevated`; items inside an `--bg-elevated` popover or menu hover to `--bg-inset`.

**Radii.** `--radius-card` 12px for cards, `--radius-control` 10px for buttons, inputs, nav items and popovers, `--radius-sm` 8px for icon tiles, dropdowns and items inside popovers, `--radius-pill` for avatars, pills and rings.

## 3. Typography

- **Font:** Inter, everywhere. Tabular numbers (`font-variant-numeric: tabular-nums`) for KPIs, tables and percentages.
- **Scale:**

| Role | Size / weight | Example |
|---|---|---|
| Page greeting | 22px / 600 | "Welcome back, Jane" |
| Top bar page title | 15px / 600 | "Overview" |
| KPI value | 26–28px / 700 | "87%", "114 / 133" (the "/ 133" part is 20px / 500) |
| Card title | 14–15px / 600 | "Compliance Progress" |
| Body / nav / table | 12–13px / 400–500 | nav items, table rows |
| Caption / delta / axis | 11px / 400–500 | "12% vs last month" |
| Form field below 640px | 16px (`--text-field`) | text typed into an input or textarea on a phone; iOS zooms into smaller fields on focus. From 640px fields use the body size. |

## 4. Layout

- **App frame:** sidebar 220px fixed + content, filling the viewport. Content padding 20px (16px below 640px); gap between cards 16px. Below 1024px the sidebar becomes a modal drawer from the left over `--scrim`, opened from a menu button in the top bar.
- **Sidebar (top → bottom):** logo · tenant label (building icon + name, static, no border) · establishment switcher (type icon, name, chevron; transparent, 1px `--border`, `--radius-control`) · navigation · flexible space · user card (avatar, name, email, chevron).
- **Navigation:** icon (outline, 18px) + label. Active item = `--teal` filled, rounded 10px, white (`--text-primary`) label and icon. Items: Overview, Establishments, Compliance, Audit Management, Regulatory Library, Change Monitor, Reports, Settings, plus **Ask the Regulation** (Lucide `MessagesSquare`), which replaces the AI Assistant card: the card is not built. Items whose screen does not exist yet are hidden, not disabled.
- **Top bar:** 56px tall, no border. Page title left; search field ("Search anything…", ⌘K hint) · notification bell · avatar right.
- **Page header:** greeting + one-line subtitle on the left; secondary action ("Export Report", icon + text) then primary action ("+ Add Evidence") on the right.
- **Overview grid:**
  1. KPI row — 5 equal cards.
  2. Donut card (≈ 1/3) + controls table card (≈ 2/3).
  3. Area chart (≈ 2/5) + upcoming list (≈ 1/4) + risk donut (≈ 1/3).
- **Feature panels** (Ask the Regulation, Simulated Inspection, Change Monitor) use the same card language. The mock's icon rail beside Ask the Regulation is not built: the app sidebar replaces it, as it replaces the AI Assistant card.
- **Ask the Regulation:** three cards in a row with a 16px gap, filling the height of main; main does not scroll, each card scrolls on its own. Conversation list 240px | conversation (flex; message column max 720px, centered) | source panel 360px, inline only while open.
  - from 1280px: all three inline;
  - 768–1279px: the source panel becomes a modal sheet from the right, 400px;
  - below 768px: the conversation list becomes a modal sheet from the left (280px, max 85vw), opened from the conversation header; the source sheet is full screen below 640px.

## 5. Components

| Component | Spec |
|---|---|
| **Card** | `--bg-card`, 1px `--border`, radius 12px, padding 16–20px. Title top-left (14–15px/600); optional control top-right (dropdown or outlined "View all →" button). No heavy shadows. |
| **KPI card** | label (secondary) + icon in a 28px rounded-square tile top-right (mint/blue outline icon) · value · delta row (↑ `--success` / ↓ `--danger` + "vs last month" in `--text-secondary`) · sparkline bottom-right **or** a progress bar. |
| **Primary button** | `--mint` fill, `--on-mint` text 13px/600, radius 10px, height 36px, optional leading icon (+). One per view. |
| **Secondary button** | transparent, 1px `--teal-border`, white text, radius 10px, optional leading icon. |
| **Status pill** | fully rounded, 1px border in the status color, background = status color at ~10% opacity, text in the status color 11–12px/500, leading glyph: ◆ diamond for Compliant and Attention/Partially Compliant, ● dot for Non-Compliant, ⊘ for Not Applicable. |
| **Time badge** | `--info-bg` fill, `--info` text, radius 6px, 11px ("In 14 days"). |
| **"NEW" badge** | `--teal-deep` fill, mint text, uppercase 10px/700, radius 4px. |
| **Dropdown / select** | `--bg-inset`, 1px `--border`, radius 8px, chevron right ("ISO 27001:2022 ⌄", "6 Months ⌄"). |
| **Table** | header row in `--text-muted` 11px; rows 12px with 1px `--border` separators, no zebra; columns: ID · name · status pill · progress bar + % · count · date. |
| **Progress bar** | 6px high, `--chart-track` track, `--chart-1` fill, rounded; percentage to the right in white. |
| **Donut** | thick ring (~18% of radius), big centered value + caption; legend to the right: color dot · label · % · (count). |
| **Area chart** | single series, `--chart-1` line 2px with dot markers, gradient fill, 0–100% axis and month labels in `--text-secondary`. |
| **Skeleton** | loading placeholder bars and tiles in `--bg-elevated`, `--radius-sm`, with a gentle opacity pulse (none under reduced motion); the container has `aria-busy="true"` and a visually hidden loading label. |
| **Small ring** | indeterminate progress, 16px: 2px `--bg-elevated` track with a mint arc, spinning; static under reduced motion. Next to a status text, for example "Signing you in…". |
| **Radial progress** | mint ring on `--chart-track`, centered % (white, 20px/700) + status text ("Evaluation In Progress…"). |
| **Stepper** | numbered circles connected by a line: "1 Analysis — 2 Findings — 3 Action Plan"; current step mint, others `--text-secondary`. |
| **Checklist** | mint filled check-circle for done, mint outlined circle for in progress, text in `--text-secondary`. |
| **Chat** | user bubble right-aligned, `--bubble-user` fill, white 13/20 text, `--radius-control`, padding 10/14, max 85% of the column, with a 32px initials avatar on `--teal` to its right. Assistant bubble left after a 32px N-mark avatar (`--bg-elevated` circle): `--bg-elevated`, 1px `--border`, `--radius-card`, padding 16; each claim a paragraph followed by its citation chips. Input: auto-growing textarea (1–6 lines, min 44px) on `--bg-inset`, 1px `--border`, `--radius-control`, with a 32px round `--mint` send button and an `--on-mint` icon inside it at the bottom right (the mock draws it blue with a white arrow; one accent wins). |
| **Citation chip** | button, 28px tall, padding 0/10, `--radius-sm`, 1px `--teal-border`, transparent; mint 12px/500 label "View source · {section}" and a 14px arrow. Hover `--bg-inset` (inside an elevated bubble). Open (`aria-expanded`): `--teal-deep` fill, mint text. A long label truncates at the chip width. |
| **System message** | refusal, unavailable and connection messages inside the assistant bubble: 16px Lucide icon in `--text-secondary`, a 13px/500 white line, a 12px `--text-secondary` hint, optionally a secondary button ("Try again"). No status colors: these are not compliance states. |
| **Conversation list row** | min 48px, `--radius-control`; title 13px/500 white, one line; date 11px `--text-secondary`, tabular. Hover `--bg-elevated`. Selected = the nav-active style (`--teal` fill) with every line in `--text-primary` (`--text-secondary` on `--teal` is only 3.6:1). |
| **Source panel** | card (or sheet) with a sticky header ("Source" 15px/600 + close button), a scrolling body (source title 12px secondary · section 15px/600 · "Version · language" 11px secondary · the stored passage in an inset block, 13/20, in its original language) and a footer (mint "Open the official page" link with an external-link icon, 11px stored-copy caption). |
| **Quote highlight** | `<mark>` in the source passage: `--teal-deep` fill, `--text-primary` text, no radius, decoration repeated across line breaks; hidden "Quoted passage:" / "End of quoted passage." around it for screen readers. |
| **Sheet** | Radix Dialog over `--scrim`, full height, `--bg-card`, 1px `--border` on the inner edge. From the left (`drawer-in`) or from the right (`sheet-in`: from `translateX(100%)`, 180ms ease-out). |
| **Alert dialog** | Radix AlertDialog over `--scrim`: `--bg-elevated`, 1px `--border`, `--radius-card`, padding 20, max width 400; title 15px/600, body 13px `--text-secondary`; right-aligned Cancel (secondary, focused first) then the confirming primary button. Buttons inside it hover to `--bg-inset`. |
| **Change alert card** | "NEW" badge, title (white 16px/600), description (secondary), outlined "View Details →", document illustration with an `--alert` "!" badge; footer row with icon + "Impact: N establishments" + chevron. |
| **Icons** | Lucide, outline, 1.5px stroke, 16–20px, `--text-secondary` by default. Mint marks the active item only on plain (unfilled) icons; on the filled active nav item the icon is white (`--text-primary`) on `--teal`. |

## 6. Motion

Subtle and functional (Framer Motion): cards fade/slide in 150–200ms on load, chart lines draw once, streaming chat text appears word by word (each claim at about 25 words per second, claims queued in arrival order, its citation chips once it has finished; the whole claim at once under reduced motion; answers loaded from history show whole), radial progress animates between values. No bouncing, no parallax. Under `prefers-reduced-motion` nothing moves: no fades, slides, skeleton pulse or spinning.

## 7. Implementation stack (recommended by the reference)

React + TypeScript · Tailwind CSS · shadcn/ui · TanStack Query · Recharts · Framer Motion · Lucide React. The reference names Next.js; the web app is a Vite SPA in front of the .NET API instead ([ADR 011](../adr/011-web-app-is-a-vite-spa.md)). What is installed today: `src/web/CLAUDE.md`.

## 8. Normetry screen inventory

| Screen | Built from |
|---|---|
| Overview | KPI row, compliance donut, controls/requirements table, compliance over time, upcoming audits, top risk categories |
| Ask the Regulation | conversation list, chat with citation chips, source panel with the quoted passage in its original language (§4 layout) |
| Documents | upload + table with type, expiry and ingestion status pills |
| Simulated Inspection | stepper + radial progress + checklist while running; findings table with status pills and Accept / Reject / Edit after |
| Change Monitor | change alert cards with impact footer |
| Establishments, Reports, Settings | same card + table language |
