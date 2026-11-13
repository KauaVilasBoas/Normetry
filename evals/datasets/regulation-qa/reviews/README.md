# Reviews

Verdicts from the third validation pass of the regulation QA dataset (see `../README.md`, "Validation").

- One file per regulation: FDA Food Code 2026, Regulation 852/2004 (both languages), RDC 216/2004.
- Each item gets a verdict, `correct`, `wrong` or `ambiguous`, with a reason.
- Wrong items are fixed in `../items.jsonl` and reviewed again. Ambiguous items are dropped. The dataset is done when every item left has a `correct` verdict, and the counts in `../README.md` match.
