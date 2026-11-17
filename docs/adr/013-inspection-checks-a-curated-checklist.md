# 013. Check a curated checklist in the simulated inspection

- **Status:** Accepted
- **Date:** 2026-11-17

## Context

The simulated inspection judges an establishment's documents against the regulation that applies
to it. The parsed corpus has 366 FDA Food Code sections, 35 sections of Regulation (EC) No
852/2004 per language and 128 RDC 216/2004 items, and many of them never apply to a restaurant
(mobile units, shellstock tags, primary production). The judge makes one model call per
requirement. The public demo runs under a monthly model spend cap of about US$ 5, and on CPU the
re-ranker alone costs seconds per search. The inspection also has to be measured: precision and
recall need a fixed list of what is being checked. Inspectors work from such lists already: the
FDA's inspection report (Form 3-A) has 56 items, each tied to Food Code sections.

## Decision

The inspection checks one curated checklist per regulation, shipped with the AI service
(`src/ai/normetry_ai/inspection/checklists/`): 47 items for the Food Code, 40 for 852/2004 and 44
for RDC 216/2004. Each item states one requirement in English, cites the corpus sections it comes
from (a test holds every path to the parsed corpus), names the establishment types it applies to,
hints at the records that show it, and carries its severity. Severity comes from the checklist,
never from the model. An independent reviewer agent briefed as a food-safety lawyer checked every
item; the reviews and the fixes sit next to the files (#95).

## Consequences

- An inspection costs one judge call per item, cents and minutes, inside the demo budget.
- Every finding names a stable item id, so the eval compares findings with expected verdicts item
  by item.
- Coverage stops at the key requirements: a breach of a provision outside the checklist is never
  reported. The interface and the README say "checklist", not "full audit".
- Checklists are data to maintain: a new regulation version, or a parser change that moves
  section paths (#114), needs the items and their review updated.

## Alternatives considered

- **Every provision of the regulation**: hundreds of calls and tens of minutes per inspection,
  about US$ 1 each, and most findings irrelevant to a restaurant.
- **A model picking the applicable sections**: not deterministic, so runs are not comparable and
  the eval has no fixed denominator.
