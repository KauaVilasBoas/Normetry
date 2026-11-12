# src/ai — Python AI service

Python 3.12+, managed with `uv` (`pyproject.toml` + `uv.lock`). FastAPI serves chat (SSE) and search; workers run ingestion, simulated inspection and change monitoring. LangGraph is used only for agents.

## Package `normetry_ai/`

- `api/` — FastAPI routes: chat over SSE, search.
- `ingestion/` — parse (Docling) → structural chunking → enrichment → embedding + `tsvector` → idempotent upsert by content hash.
- `retrieval/` — hand-written hybrid search: pgvector top-k + full-text top-k → Reciprocal Rank Fusion → multilingual re-ranker. No framework retrievers.
- `agents/` — LangGraph graphs (simulated inspection, change monitor) with Postgres checkpoints.
- `llm/` — the only place that calls model providers: routing (cheap vs strong model), token and cost accounting per tenant, budget enforcement.

## Boundaries

- Not public. Every request must carry the internal token; reject it otherwise. Tenant, establishment and jurisdiction come only from the internal headers set by YARP.
- Writes only schema `ai`, through Alembic migrations. Results for the core (findings, document status) are published as queue events.
- The unit-of-work helper sets the tenant per transaction (`rls-table` skill). Workers take the tenant from the queue message.
- The public corpus (`tenant_id IS NULL`) is written only by the corpus ingestion job, using the `corpus_ingestor` role.

## LLM safety

- Document text enters prompts delimited as data, never as instructions. Agents have no write tools; every state change goes through human review.
- Answers are structured: `answer` plus `claims`, each with the chunk ids that support it. Drop any claim citing an id that was not retrieved. Below the re-ranker threshold, refuse instead of guessing.
- Mask personal names in logs and traces. Never log secrets or full documents.
- A change to prompts, chunking, retrieval, embeddings or agents runs the evals (`eval-runner` agent) and reports the deltas.

## Tooling

- `uv run pytest` · `uv run ruff check` · `uv run ruff format`. The hook runs Ruff format on every edited `.py` file once `src/ai/.venv` exists.
- Type hints on all public functions.
