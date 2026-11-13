# Regulation QA golden dataset

59 questions that a restaurant or food business operator could ask about the regulation that applies to their establishment. Each one comes with the behavior expected from the chat (answer or refuse), a short expected answer, and, for answers, literal quotes from the regulation that support it. Retrieval and generation in phase 1 are measured against this set (phase 1 design, "Golden dataset").

It is not legal advice. The expected answers say only what the quoted text says.

## Files

- `items.jsonl`: one item per line.
- `reviews/`: verdicts from the third validation pass (see below).

## Item fields

| Field | Content |
|---|---|
| `id` | jurisdiction prefix and number: `us-001`, `eu-001`, `br-001` |
| `jurisdiction` | the establishment's jurisdiction: `US`, `EU` or `BR` |
| `language` | `en` or `pt`. US items are in English, BR items in Portuguese, EU items in either |
| `type` | `direct`, `two_provisions`, `not_covered`, `wrong_jurisdiction`, `misleading_premise` or `follow_up` |
| `turns` | the user's messages. Only the last one is scored; earlier ones are context. One turn, except `follow_up` (2 or 3) |
| `expected_behavior` | `answer` or `refuse` |
| `expected_answer` | short, in the item's language. For a refusal, what the chat should tell the user |
| `provisions` | for answers, a list of `{source_id, section_path, quote}`; empty for refusals |

`source_id` is a source id from `corpus/manifest.json`, and an item only cites sources of its own jurisdiction and language: EU items in Portuguese quote `eu-reg-852-2004-pt`, and those in English quote `eu-reg-852-2004-en`. `section_path` is the path the corpus parser gives the section (`3-501.16`, `Annex II, Chapter IX`, `4.8.15`). A quote is copied from that section's parsed text and runs 8 to 60 words, so it can be found in whatever chunk ends up holding it.

Item types:

- `direct`: one fact from one provision (a temperature, a time limit, a requirement).
- `two_provisions`: the answer needs at least two provisions, usually from different sections.
- `not_covered`: the ingested text does not answer the question. Expected: refuse.
- `wrong_jurisdiction`: only another jurisdiction's ingested text answers it. Expected: refuse, with no citation from the other regulation.
- `misleading_premise`: the question assumes something false. Expected: an answer that corrects it with the right provision.
- `follow_up`: 2 or 3 turns; the last one only makes sense with the earlier ones ("And chicken?"). It can expect an answer or a refusal.

## Counts

| | direct | two_provisions | not_covered | wrong_jurisdiction | misleading_premise | follow_up | total |
|---|---|---|---|---|---|---|---|
| US, en (FDA Food Code 2026) | 6 | 3 | 3 | 2 | 2 | 4 | 20 |
| EU, en (Regulation 852/2004) | 4 | 2 | 1 | 1 | 1 | 1 | 10 |
| EU, pt (Regulamento 852/2004) | 3 | 1 | 2 | 1 | 1 | 2 | 10 |
| BR, pt (RDC 216/2004) | 7 | 3 | 3 | 1 | 2 | 3 | 19 |
| Total | 20 | 9 | 9 | 5 | 6 | 10 | 59 |

By language: 30 in English, 29 in Portuguese. By expected behavior: 44 answers (83 quotes) and 15 refusals (one of them a follow-up). Ids are not renumbered when an item is dropped, so `br-015` is missing. `uv run python -m normetry_ai.evals.dataset` (from `src/ai`) prints the same counts.

## How the items were built

- Source text: the sections that `uv run python -m normetry_ai.ingestion.corpus` produces from the files pinned in `corpus/manifest.json`, after `normalize_text`. Quotes come from this text, not from the PDFs or the web page, so FDA quotes keep the parsed markers (`*` before defined terms, `P` and `Pf` after provisions).
- Scope is what phase 1 ingests: FDA Food Code chapters 1 to 8 (no annexes), the articles and annexes of Regulation 852/2004, and items 1 to 4.12 of the RDC 216/2004 Technical Regulation. "Not covered" means not covered by these parts.
- Questions are written the way an operator talks ("How fast does the chili have to cool down?"), in the item's language. EU Portuguese items use European Portuguese and BR items Brazilian Portuguese.
- An expected answer restates what its quotes say, with the provision's own conditions ("where necessary", "if there is any likelihood of contamination"). Where the question names a dish (chili, potato salad), the answer states the rule for the category the text uses (time/temperature control for safety food) and does not claim the dish belongs to it.
- `wrong_jurisdiction` topics are ones another jurisdiction's ingested text covers. The expected answer names that provision so reviewers can check it, but the chat must not cite it.
- The FDA parser from #46 folds sections numbered with three digits after the point (`4-501.114`, `6-501.115`, 35 in all) into the section before them (`4-501.19`, `6-501.19`). No item quotes those sections, so a parser fix will not move any quote.

## Validation

1. Script: `uv run python -m normetry_ai.evals.dataset` (from `src/ai`) checks every field, type and behavior rule, and that every quote, after `normalize_text`, appears in the named section of the named source. It exits 1 on any problem. `tests/test_dataset.py` runs it on the real corpus in CI and checks that changing one word of any quote makes it fail.
2. Reread: every item was read again against its section text, including the text around each quote, to confirm that the quote supports the expected answer on its own terms and that the answer adds nothing the text does not say. For every refusal, the source in the item's language was searched for the topic and its synonyms (table below), and no ingested section answers it.
3. Independent review: one reviewer per regulation receives only the source text and the items, and rules each item correct, wrong or ambiguous with a reason. Wrong items are fixed and ambiguous ones dropped. The verdicts go in `reviews/`. Result: 852/2004, 20 correct; RDC 216/2004, 19 correct and `br-015` dropped as ambiguous; FDA Food Code, 15 correct and 5 wrong, fixed and reviewed again (see `reviews/fda-food-code-2026.md`).

### Searches behind the refusals

Case-insensitive regular expressions over every parsed section of the item's source. A hit is listed when the word appears in an unrelated sense.

| Item | Topic | Searched | Result |
|---|---|---|---|
| us-010 | minimum age for kitchen staff | `\bage\b\|\bminors?\b\|years old\|teen\|youth\|child labou?r` | only highly susceptible populations and children's juice (1-201.10, 3-801.11) |
| us-011 | liquor license | `liquor\|alcohol\|\bbeer\b\|\bwine\b\|licen[cs]e` | only a wine bottle as condiment container, beer brewing materials, a licensed health practitioner |
| us-012 | consecutive working hours | `\bshifts?\b\|working hours\|work hours\|hours of work\|consecutive hours\|overtime\|rest break\|meal break` | only "cleanup at the end of a shift" (4-301.12) |
| us-013 | frying oil temperature and change | `\bfry\|fryer\|frying\|\boils?\b\|shortening\|smoke point` | only oil filtering equipment, oil tanks and lines, refined-oil allergen rule; no temperature limit or change rule |
| us-014 | food safety culture | `culture` | only aquaculture, cultured dairy, throat culture. 2-103.11 (person in charge, active managerial control) has no food safety culture requirement |
| us-020 | language of the handwashing sign | `spanish\|language\|bilingual\|translat` | none (6-301.14 requires the sign, says nothing on language) |
| eu-007 | amount of fines | `\bfines?\b\|penalt\|sanction\|infringement\|€\|\bEUR\b` | none |
| eu-008 | cooking temperature for chicken | `°\|ºC\|degree\|\bcook\|poultry\|chicken\|\bmeat` | only "a high degree of personal cleanliness" |
| eu-015 | licence to sell alcohol | `licença\|álcool\|alcoól\|bebida\|vinho\|cerveja` | none |
| eu-016 | consecutive working hours | `\bhoras?\b\|horário\|turno\|descanso\|jornada` | none |
| eu-017 | days cooked food keeps in the fridge | `\bdias?\b\|frigorífico\|refrigera\|°\|ºC\|graus\|cozinhad` | only definitions, entry into force, refrigerated storage space (Annex II, Chapter IX, point 5); no time limit |
| br-011 | operating licence (alvará) | `alvará\|licença\|licenciamento\|autoriza\|funcionamento` | only equipment "funcionamento" and a reference title (3.11) |
| br-012 | minimum age for kitchen staff | `idade mínima\|\banos\b\|menor de\|maior de\|menores` | none |
| br-013 | fire extinguishers | `extintor\|incêndio\|incendio\|\bfogo\b\|chama` | none |
| br-014 | donating leftover food | `doação\|doar\|doad\|\bsobra\|\bONG\b\|excedente` | none |
