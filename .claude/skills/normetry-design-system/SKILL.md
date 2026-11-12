---
name: normetry-design-system
description: Normetry's visual design system — dark navy surfaces, mint accent, Inter, card/KPI/table/status-pill/chat components — taken from the approved mock in docs/design/reference.png. Use for ANY frontend work in src/web (new screen, component, styling, chart, empty state) and for reviewing UI changes, so the app matches the reference exactly.
---

# Normetry design system

The look of the app is fixed by an approved mock. Your job is to reproduce it, not to reinterpret it.

## Before designing or coding UI

1. Open `docs/design/reference.png` (Read it as an image) and find the closest existing pattern in it.
2. Read `docs/design/DESIGN.md` for exact tokens, typography, layout and component specs.
3. If `src/web` already has the component, reuse it. New visual primitives belong in the shared UI layer, never inline in a screen.

## Hard rules

- **Tokens only.** Colors, radii and font sizes come from the tokens in `DESIGN.md` (CSS variables / Tailwind theme). No raw hex values or arbitrary pixel sizes in components.
- **Dark theme only.** Surfaces go `--bg-canvas` → `--bg-sidebar` / `--bg-app` → `--bg-card` → `--bg-inset` / `--bg-elevated`. Never put a light surface in the app.
- **One accent.** Mint (`--mint`) is for the primary action, links, focus and progress. One primary button per view. Text on mint is `--on-mint` (dark), never white.
- **Status colors mean status.** Green/amber/red/gray only for Compliant / Partially Compliant or Attention / Non-Compliant / Not Applicable and for deltas. Never decorative.
- **Data is blue.** Charts and progress bars use `--chart-1` / `--chart-2`; red only for the non-compliant share.
- **Inter + tabular numbers** for every number in KPIs, tables and charts.
- **Lucide outline icons**, 1.5px stroke, 16–20px.
- **Cards, not boxes in boxes.** One card level; inner sections use `--bg-inset` or a divider, never a nested bordered card.
- **Mock content is placeholder.** Use Normetry's domain (FDA Food Code, EU 852/2004, RDC 216, establishments, SOPs, findings), not ISO 27001 / Acme / Jane.

## Normetry-specific UI rules

- Every AI claim shows its citation chip ("View source →") that opens the source passage in its original language.
- Inspection findings show their review state (pending, accepted, rejected, edited) and the Accept / Reject / Edit actions.
- Long-running work shows the radial progress or stepper pattern, fed by real-time updates.
- Interface text in PT and EN; regulation excerpts stay in their original language.

## Accessibility (non-negotiable even where the mock is silent)

- Text contrast ≥ 4.5:1 (≥ 3:1 for large text and UI borders). `--text-muted` is for non-essential text only.
- Status is never color-only: pills keep their label and glyph.
- Visible mint focus ring on every interactive element; full keyboard navigation; labels on inputs and icon-only buttons.

## Review checklist (use when reviewing a UI change)

1. Layout matches the reference pattern (spacing 16/20px, sidebar 220px, card radius 12px).
2. Only tokens used; no raw hex, no off-scale font sizes.
3. Correct component variant (primary vs secondary button, status pill glyph, badge type).
4. Status/data color semantics respected; one primary action.
5. Numbers tabular, icons Lucide outline, Inter everywhere.
6. Citations, review states and progress patterns present where the screen needs them.
7. Accessibility items above.
8. Content uses Normetry's domain, PT/EN ready.
