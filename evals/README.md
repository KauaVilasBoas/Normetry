# Evals

No eval makes a paid API call in CI (phase 1 design, "Evals").

| Suite | Measures | Command (from `src/ai`) | In CI |
|---|---|---|---|
| Golden dataset check | every item follows the schema, every quote is in its section | `uv run python -m normetry_ai.evals.dataset` | every pull request (job `ai`, `tests/test_dataset.py`) |
| Retrieval | recall@k and MRR of each retrieval step against the golden dataset | `uv run pytest -m eval` | pull requests that touch ingestion, retrieval, models, settings or the dataset (job `ai (retrieval eval)`) |

Generation, inspection and security evals come with the features they measure.

## Dataset

`datasets/regulation-qa/`: 59 questions, 44 answerable with 83 literal quotes, 15 refusals. Its README has the schema and how the items were built and reviewed.

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

## Reports

`reports/`: one Markdown file per baseline run, named `<date>-<suite>-<variant>.md`. The newest file of a suite is its baseline. CI writes its run to the job summary and does not commit it.

| Report | What |
|---|---|
| [2026-11-13-retrieval-bge-m3.md](reports/2026-11-13-retrieval-bge-m3.md) | Retrieval baseline with BGE-M3: vector, full-text and fusion measured; the re-ranker could not be downloaded for that run |
