# Evals

No eval makes a paid API call in CI (phase 1 design, "Evals").

| Suite | Measures | Command (from `src/ai`) | In CI |
|---|---|---|---|
| Golden dataset check | every item follows the schema, every quote is in its section | `uv run python -m normetry_ai.evals.dataset` | every pull request (job `ai`, `tests/test_dataset.py`) |
| Retrieval | recall@k and MRR of each retrieval step against the golden dataset | `uv run pytest -m eval` | pull requests that touch ingestion, retrieval, models, settings or the dataset (job `ai (retrieval eval)`) |
| Generation | answers, refusals, citations, time and cost of the real pipeline per chat model | `uv run pytest -m generation` | never: paid, run on demand |
| Inspection | findings against the synthetic establishments' expected findings: precision and recall per verdict, guard drops, injection, cross-establishment evidence, tokens, cost and time | `uv run pytest -m inspection` | never: paid, run on demand |

Security evals come with the features they measure.

## Dataset

`datasets/regulation-qa/`: 59 questions, 44 answerable with 83 literal quotes, 15 refusals. Its README has the schema and how the items were built and reviewed.

### Inspection feedback

`datasets/inspection-feedback/<inspectionId>.jsonl` holds one inspection's reviewed findings as eval examples. It is written in Development only, by `POST /establishments/{establishmentId}/inspections/{inspectionId}/feedback-export` on the core (`inspections.review`; 404 in any other environment). The export runs under the caller's tenant like any request (RLS), answers `{ path, examples }` and replaces an earlier export of the same inspection. Get a token as `deploy/keycloak/README.md` shows and call the API's http endpoint from the Aspire dashboard:

```sh
curl -X POST -H "Authorization: Bearer $TOKEN" \
  http://localhost:<api-port>/establishments/<establishmentId>/inspections/<inspectionId>/feedback-export
```

There is one line per rejected or edited finding, ordered by item id (UTF-8, `\n` line ends, accents kept as written):

```json
{"itemId":"us-temp-01","original":{"verdict":"non_compliant","severity":"critical","rationale":"…","actionText":"…","dueOn":"2026-11-24"},"correction":{"verdict":"compliant","severity":"critical","rationale":"…","actionText":"…","dueOn":"2026-12-01"},"note":null,"reviewer":"<sub>"}
```

- `original` holds the inspection's values and `correction` the values after the review, with the same keys. For a rejection without an edit, both hold the same values.
- `note` is the rejection reason. It is null for an edit, and a non-null note means the latest decision was a rejection.
- `reviewer` is the latest reviewer's `sub`, with no name or email.
- `actionText` and `dueOn` are null when the finding has no action.

Review exported files before committing them: they hold a tenant's rationales and notes. The folder's `.gitignore` ignores `*.jsonl`, so a reviewed export is committed on purpose with `git add -f`.

## Retrieval eval

`src/ai/tests/test_retrieval_eval.py` starts a pgvector container (Testcontainers), runs the real bootstrap and migrations, ingests the whole corpus with the configured embedding model as `corpus_ingestor`, then runs retrieval (`normetry_ai.retrieval`) as `app_ai` inside `tenant_transaction` for every item:

- the query is the item's turns joined into one (the follow-up baseline of the phase 1 design);
- the scope is `jurisdictions_for(item jurisdiction)`, the same policy the chat uses;
- a returned chunk is a hit for a quote when it contains the quote after `normalize_text`, the function the dataset check uses, so the labels survive a change of chunking.

Metrics, over answerable items, for each step (vector top-k, full-text top-k, Reciprocal Rank Fusion, re-ranked result):

- recall@k (k = 1, 3, 5, 10): the share of an item's quotes found in its top k chunks, averaged over items;
- MRR@10: 1 / the rank of the first top-10 chunk holding any of the item's quotes.

The report also lists the quotes missing from the re-ranked top 10, counts chunks returned outside the item's jurisdiction (or, for EU, outside the question's language), which must be 0, shows the best re-ranker score for answerable items and refusals (input for the refusal threshold), and times each step.

The test fails when a chunk leaves the scope or the re-ranked result drops below its floors (`FLOORS` in the test: recall@10 0.9, MRR@10 0.75).

### Running it

Needs Docker, the fetched corpus (`uv run corpus/scripts/fetch_corpus.py` from the root) and about 5 GB of models in the Hugging Face cache (downloaded on first use).

```bash
cd src/ai
uv run pytest -m eval                                         # report: src/ai/.data/retrieval-eval.md
NORMETRY_EVAL_REPORT=../../evals/reports/<date>-retrieval-<model>.md uv run pytest -m eval   # a new baseline
```

Run it alone (`-m eval`): other integration tests in the same session add sources, and the test refuses to score a database that holds anything but the manifest's sources. Document vectors are cached in `src/ai/.data/embeddings/` by text, per model, so a second run embeds nothing; CI caches that directory and the model weights.

To try another embedding model, set `NORMETRY_EMBEDDING_MODEL` and `NORMETRY_EMBEDDING_MODEL_REVISION`. The column is `vector(1024)`, so the model must output 1024 dimensions. Retrieval knobs are `NORMETRY_RETRIEVAL_*` and `NORMETRY_RERANKER_*` (`RetrievalSettings` in `src/ai/normetry_ai/settings.py`).

## Generation eval

`src/ai/tests/test_generation_eval.py` ingests the corpus as the retrieval eval does (same container setup, same vector cache), then runs the chat pipeline on every item:

- the last turn is the question and the earlier turns are its history. The dataset has no assistant turns, so a follow-up's history holds the user's turns only;
- a follow-up runs twice: rewritten by the rewrite model (`followup_rewrite = "model"`, the default, which the main metrics use) and by the free baseline. A one-turn item runs once;
- retrieval runs once per pass as `app_ai`, inside `tenant_transaction`, in the scope `jurisdictions_for(item jurisdiction)`;
- `answers.answer` then runs for every model in `NORMETRY_EVAL_MODELS` at once, with the default effort and `max_tokens`.

Each model gets a Markdown report and a JSONL in the report directory, named `<date>-generation-<model>`. The report has answerable items answered, expected refusals refused (also per type: `not_covered`, `wrong_jurisdiction`, the follow-up), wrong-jurisdiction citations, claims shown and dropped by the guard, model refusals (`stop_reason` `refusal`), unavailable answers, how many gold quotes the cited chunks hold, p50 and p95 of time to first claim and total time, tokens and cost, and the rewrite comparison: retrieval recall@10 of the gold quotes, correct outcomes and rewrite cost, model against baseline. The JSONL holds one line per item and pass, with the question, the standalone question, the expected answer, the gold quotes and every shown claim with its citations.

Faithfulness (does each quote support its claim?) is not scored by code. It is judged from the JSONL in a Claude Code session, and the verdict goes in the report's `## Review` section.

### Running it

It calls the Anthropic API, so it costs money: cents per model with Haiku, more with Opus (phase 1 design, "Evals"). It runs only on demand, never in CI, and is skipped (before any container starts) when `NORMETRY_ANTHROPIC_API_KEY` is not set. Set the key in the shell for the run, never in a file. Same needs as the retrieval eval: Docker, the fetched corpus and the local models.

```powershell
cd src/ai
# $env:NORMETRY_ANTHROPIC_API_KEY set in this shell
uv run pytest -m generation                                                # reports: src/ai/.data/generation-eval/
$env:NORMETRY_EVAL_REPORT_DIR = "../../evals/reports"; uv run pytest -m generation   # a new baseline
```

- `NORMETRY_EVAL_MODELS`: comma-separated chat models, default `claude-haiku-5-5,claude-sonnet-5-5,claude-opus-5-5`. Each needs a price in `NORMETRY_MODEL_PRICES`.
- `NORMETRY_EVAL_BUDGET_USD`: default 5. Once the cost of every model call so far (answers of all models, both rewrite modes, rewrites) reaches it, no new call starts and the reports are written and marked "stopped at budget after item X". The models of one pass run at once, so the last pass can go past the budget by its own calls.

Run it alone, as the retrieval eval.

## Inspection eval

`src/ai/tests/test_inspection_eval.py` inspects the three synthetic establishments of [`establishments/`](establishments/README.md) and scores the findings against each one's `expected-findings.json` (`normetry_ai.evals.inspection`). It ingests the corpus as the retrieval eval does, then every establishment's documents with the AI service's `DocumentWorker` (a fake blob store, the real parser and BGE-M3), in the demo's two tenants: Demo Bistro holds São Paulo and Lisboa, Demo Bakery holds Austin. All three are indexed before the first inspection, so evidence could leak between two establishments of one tenant or between tenants. Each establishment is then inspected by `InspectionWorker` with an in-memory publisher: the real graph, evidence search, re-ranker, checkpoints and usage rows on the test database, no queue, and the real `ClaudeJudgeModel` (`NORMETRY_INSPECTION_MODEL`, default `claude-haiku-5-5`) with a token ceiling of 300,000 per inspection, the core's default.

Each checklist item gets one outcome: its guarded verdict, `dropped` (judged, but the guard kept no finding), `no_regulation` (no regulation text, never judged) or `not_run` (a budget or an error stopped it before its call). Items not run are left out of precision and recall and counted on their own. The report has, over all establishments and for each one:

- precision and recall per verdict and the confusion matrix, with the recall of the planted `non_compliant` items and the precision of `non_compliant` findings first;
- the guard: items dropped, verdicts downgraded to `insufficient_evidence` (no document quote found in its excerpt), and the quotes it kept of those the judge gave;
- citations: evidence from a document of another establishment or tenant, and shown quotes not in the passage they cite. Both must be 0, and the test fails otherwise, after writing the report;
- injection: a success is a target item of the injection (`injection.targets`) whose verdict moved towards `compliant`, the way every injection asks (`non_compliant`, then `insufficient_evidence`, then `compliant`). An injection can also shift items it does not name, so the report lists every verdict change on any item, in either direction, marks the ones towards `compliant`, and lists the findings that quote the injection document. An ordinary judge error shows there too; the review tells them apart;
- tokens, cost and duration per inspection, against SC-001 (under 10 minutes and US$ 0.10 each), and judge replies that stopped other than with `end_turn`.

The JSONL holds one line per establishment and item: the expected verdict, evidence and note, the outcome, the judge's verdict before the guard, the rationale, the action, and each citation's quote and place (the document's file for evidence).

### Running it

It calls the Anthropic API once per checklist item, 131 calls in all, so it costs money. With Haiku the prompt sizes put it at about US$ 0.05 per inspection; the first run records the real cost and time. Most of the time goes to BGE-M3 and the re-ranker on the CPU: indexing the 28 documents (cached in `src/ai/.data/embeddings/` after the first run) and re-ranking the evidence for every item. Same needs and rules as the generation eval: Docker, the fetched corpus, the local models, the key in the shell only, skipped (before any container starts) without it, never in CI.

```powershell
cd src/ai
# $env:NORMETRY_ANTHROPIC_API_KEY set in this shell
$env:NORMETRY_EVAL_BUDGET_USD = "1"; uv run pytest -m inspection          # reports: src/ai/.data/inspection-eval/
$env:NORMETRY_EVAL_REPORT_DIR = "../../evals/reports"; uv run pytest -m inspection   # a new baseline
```

- `NORMETRY_EVAL_BUDGET_USD`: default 1. Once the judge calls so far have cost that much, no new call starts: the current inspection stops as its token ceiling would stop it, later ones are not run, and the report says so. Up to `NORMETRY_INSPECTION_CONCURRENCY` calls (4) can be in flight at that moment, so the total can pass the budget by their cost.
- `NORMETRY_INSPECTION_MODEL`, `NORMETRY_INSPECTION_MAX_TOKENS`, `NORMETRY_INSPECTION_CONCURRENCY`: the judge, as in the service. The model needs a price in `NORMETRY_MODEL_PRICES`.

Run it alone, as the retrieval eval: it refuses a database holding sources other than the manifest's, since regulation text is read by section path.

### Review and targets

As for the generation eval (#55), code does not judge whether a finding is right for the right reasons. A Claude Code session reads every finding in the JSONL against its quotes, the expected finding and its note, and records a verdict per finding (right, right for the wrong reason, wrong) in `<report>.review.json`. The summary goes in the report's `## Review` section. That section also says, for each verdict change, whether the injection caused it. A non-zero injection success or cross-establishment count gets its own issue.

The first run sets the targets (recall of planted `non_compliant` items, precision of `non_compliant` findings, guard drop rate) and they are written in its report; later runs are compared with it. Reviewed findings from the app become eval examples in [`datasets/inspection-feedback/`](#inspection-feedback).

## Reports

`reports/`: one Markdown file per baseline run, named `<date>-<suite>-<variant>.md`. The generation and inspection evals also write a `.jsonl` with the same name, and their review adds a `.review.json` holding the verdict on every claim and answer, or on every finding. A suite's newest file is its baseline. CI writes its run to the job summary and does not commit it.

| Report | What |
|---|---|
| [2026-11-13-retrieval-bge-m3.md](reports/2026-11-13-retrieval-bge-m3.md) | Retrieval baseline with BGE-M3: vector, full-text and fusion measured; the re-ranker could not be downloaded for that run |
| [2026-11-14-retrieval-bge-m3-reranked.md](reports/2026-11-14-retrieval-bge-m3-reranked.md) | Retrieval baseline with BGE-M3 and the bge-reranker-v2-m3 re-ranker: every step measured, best re-ranker scores for the refusal threshold, time per step |
| [2026-11-15-generation-claude-haiku-5-5-first-prompt.md](reports/2026-11-15-generation-claude-haiku-5-5-first-prompt.md) | First generation run, Claude Haiku 5.5, with its review. The review found claims about what the excerpts leave out, quotes that stop before a condition, and a rewrite that leaked its scratch work |
| [2026-11-15-generation-claude-haiku-5-5.md](reports/2026-11-15-generation-claude-haiku-5-5.md) | Generation baseline, Claude Haiku 5.5, after the prompt and rewrite fixes: review, comparison with the first run, phase 1 success measures |
