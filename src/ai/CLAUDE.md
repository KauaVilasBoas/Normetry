# src/ai — Python AI service

Python 3.12+, managed with `uv` (`pyproject.toml` + `uv.lock`). FastAPI serves chat (SSE) and search; workers run ingestion, simulated inspection and change monitoring. LangGraph is used only for agents.

## Package `normetry_ai/`

Flat layout: `normetry_ai/` and `tests/` sit next to `pyproject.toml`. A subpackage is created by the first feature that needs it.

- `settings.py` — pydantic-settings, env prefix `NORMETRY_` (also read from `src/ai/.env`; placeholders in `.env.example`). Secrets are `SecretStr`.
- `telemetry.py` — FastAPI traces over OTLP (gRPC), on only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- `api/` — FastAPI app factory `create_app`, the internal-token ASGI middleware (`auth.py`) and routes: `/health` now; chat over SSE and search later.
- `ingestion/` — parse (Docling) → structural chunking → enrichment → embedding + `tsvector` → idempotent upsert by content hash.
- `retrieval/` — hand-written hybrid search: pgvector top-k + full-text top-k → Reciprocal Rank Fusion → multilingual re-ranker. No framework retrievers.
- `agents/` — LangGraph graphs (simulated inspection, change monitor) with Postgres checkpoints.
- `llm/` — the only place that calls model providers: routing (cheap vs strong model), token and cost accounting per tenant, budget enforcement.

## Boundaries

- Not public. Every request must carry the internal token in `X-Normetry-Internal-Token` (exactly once). A pure ASGI middleware rejects every http and websocket scope without it before routing and body parsing (401, also for unknown paths). Do not move the check into route dependencies. OpenAPI and docs routes stay off. `NORMETRY_INTERNAL_TOKEN` (32+ characters) is required: without it the app does not start. Tenant, establishment and jurisdiction come only from the internal headers set by YARP.
- Writes only schema `ai`, through Alembic migrations (`migrations/`, version table `ai.alembic_version`, autogenerate limited to schema `ai`). Results for the core (findings, document status) are published as queue events.
- Migrations run as the owner role (`NORMETRY_MIGRATIONS_DATABASE_URL`, read only by `migrations/env.py`, no fallback); the app connects as `app_ai` (`NORMETRY_DATABASE_URL`, DML only). The owner role has no privileges on schema `core`, enforced by the bootstrap SQL (#9/#13).
- The unit-of-work helper sets the tenant per transaction (`rls-table` skill). Workers take the tenant from the queue message.
- The public corpus (`tenant_id IS NULL`) is written only by the corpus ingestion job, using the `corpus_ingestor` role.

## LLM safety

- Document text enters prompts delimited as data, never as instructions. Agents have no write tools; every state change goes through human review.
- Answers are structured: `answer` plus `claims`, each with the chunk ids that support it. Drop any claim citing an id that was not retrieved. Below the re-ranker threshold, refuse instead of guessing.
- Mask personal names in logs and traces. Never log secrets or full documents.
- A change to prompts, chunking, retrieval, embeddings or agents runs the evals (`eval-runner` agent) and reports the deltas.

## Tooling

From `src/ai`:

- `uv sync` · `uv run pytest` · `uv run ruff check` · `uv run ruff format` (`--check` in CI). The hook runs Ruff format on every edited `.py` file once `src/ai/.venv` exists.
- Run: `uv run uvicorn normetry_ai.api:create_app --factory` with `NORMETRY_INTERNAL_TOKEN` set.
- Migrations: `uv run alembic revision -m "..."` · `uv run alembic upgrade head`, with `NORMETRY_MIGRATIONS_DATABASE_URL` (owner role, `postgresql+psycopg://...`). New revisions are import-sorted and formatted by Ruff post-write hooks.
- Tests stay offline: build the app with `create_app(Settings(..., _env_file=None))` and use FastAPI's `TestClient`.
- Type hints on all public functions.
