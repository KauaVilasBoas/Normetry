# src/ai: Python AI service

Python 3.12+, managed with `uv` (`pyproject.toml` + `uv.lock`). FastAPI serves chat (SSE) and search; workers run ingestion, simulated inspection and change monitoring. LangGraph is used only for agents.

## Package `normetry_ai/`

Flat layout: `normetry_ai/` and `tests/` sit next to `pyproject.toml`. A subpackage is created by the first feature that needs it.

- `settings.py`: pydantic-settings, env prefix `NORMETRY_` (also read from `src/ai/.env`; placeholders in `.env.example`). Secrets are `SecretStr`.
- `telemetry.py`: FastAPI traces over OTLP (gRPC), on only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- `db.py`: the unit of work.
  - `create_db_engine(settings)` builds the pooled engine for `NORMETRY_DATABASE_URL` (`app_ai`). A pool `checkin` listener runs `RESET app.tenant_id` (and commits) on every returned connection, so a stray session-level `SET` never reaches the next checkout.
  - `tenant_transaction(engine, tenant_id)` opens a transaction, runs `SELECT set_config('app.tenant_id', :tenant, true)` and yields the connection; it commits on success and rolls back on error. It is the only code that sets `app.tenant_id`. The tenant must be a `uuid.UUID` other than the nil UUID (parse header or message values first). Every query on tenant data runs inside it. Outside it no tenant is set, so `app_ai` sees only the public corpus and every write fails.
  - `check_runtime_role(connection)` raises when the connected role is a superuser, has BYPASSRLS, or owns schema `ai` or anything in it. The FastAPI lifespan runs it once at startup when `NORMETRY_DATABASE_URL` is set; without it the app starts with no database (health only).
- `api/`: FastAPI app factory `create_app`, the internal-token ASGI middleware (`auth.py`), the `request_context` dependency (`context.py`) and routes: `/health` now; chat over SSE and search later.
- `ingestion/`: parse into sections → structural chunking → enrichment → embedding + `tsvector` → idempotent upsert by content hash. Built so far: the parse step (see "Corpus parsing").
  - `normalize.py`: `normalize_text` (NFC, line-break hyphenation joined, whitespace collapsed). The golden dataset's quote check uses the same function, so keep it standard-library only and importable on its own.
  - `corpus.py`: `parse_corpus()` / `parse_source(source)` read `corpus/manifest.json`, refuse a file whose SHA-256 does not match, and return `Section`s (source id, jurisdiction, language, version, `section_path`, normalized text) in document order. `uv run python -m normetry_ai.ingestion.corpus` writes them all to `.data/corpus-sections.jsonl` (git-ignored) for inspection.
- `retrieval/`: hand-written hybrid search: pgvector top-k + full-text top-k → Reciprocal Rank Fusion → multilingual re-ranker. No framework retrievers.
- `agents/`: LangGraph graphs (simulated inspection, change monitor) with Postgres checkpoints.
- `llm/`: the only place that calls model providers: routing (cheap vs strong model), token and cost accounting per tenant, budget enforcement.

## Boundaries

- Not public. Every request must carry the internal token in `X-Normetry-Internal-Token` (exactly once). A pure ASGI middleware rejects every http and websocket scope without it before routing and body parsing (401, also for unknown paths). Do not move the check into route dependencies. OpenAPI and docs routes stay off. `NORMETRY_INTERNAL_TOKEN` (32+ characters) is required: without it the app does not start. Tenant and user come only from the internal headers set by YARP, read through the `request_context` dependency (`api/context.py`). Header names and the rules for both sides: `src/shared/contracts/internal-http.md`.
- Writes only schema `ai`, through Alembic migrations (version table `ai.alembic_version`). Results for the core (findings, document status) are published as queue events.
- Migrations run as the owner role (`NORMETRY_MIGRATIONS_DATABASE_URL`, read only by `migrations/env.py`, no fallback); the app connects as `app_ai` (`NORMETRY_DATABASE_URL`, DML only). The owner role is `ai_owner`: it owns schema `ai` (created by the bootstrap in `deploy/postgres/init/`, so Alembic can write `ai.alembic_version`) and has no privileges on schema `core`. `NORMETRY_MIGRATIONS_DATABASE_URL` is only ever set for a migration step, never on the running service, because the owner can switch RLS off: set it in the shell for one `uv run alembic ...` command, or let the AppHost's `ai-migrate` step set it. It never goes in `src/ai/.env` (`MigrationSettings` does not read that file, and `.env.example` has no owner URL).
- Grants follow least privilege. The bootstrap's default privileges give `app_ai` DML on every new `ai` table, so migrations revoke what it must not have: everything on `ai.alembic_version`, and UPDATE on `ai.chunks` (no update policy; a new version replaces chunks with delete and insert). `corpus_ingestor` has no default privileges: migrations grant it each corpus table explicitly.
- Tenant-owned tables follow the `rls-table` skill: `ENABLE` and `FORCE ROW LEVEL SECURITY`, and policies scoped `TO app_ai` or `TO corpus_ingestor`, never to PUBLIC. Policies read the tenant as `(SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)`: the scalar subquery is evaluated once per statement, not once per row. Workers take the tenant from the queue message.
- The public corpus (`tenant_id IS NULL`, `source_type = 'regulation'`, enforced by a CHECK) is written only by the corpus ingestion job, using the `corpus_ingestor` role. `app_ai` reads it but cannot write it.
- Chunk ids are generated by the database (`DEFAULT gen_random_uuid()`, read back with `RETURNING id`) and never taken from callers, messages or documents. Upserts name their target, `ON CONFLICT ON CONSTRAINT chunks_source_content_key DO NOTHING`: a bare `ON CONFLICT DO NOTHING` also matches the primary key and would turn a clash with another tenant's id into a silent 0-row insert.

### Retrieval under RLS

Notes for the retrieval work; none of this exists yet.

- One HNSW index over all tenants returns an approximate candidate list (`hnsw.ef_search` long) and RLS filters it afterwards, so a large tenant or the corpus can crowd another tenant's rows out of the results.
- Plan: a partial HNSW index for the corpus (`WHERE tenant_id IS NULL`); exact KNN for tenant rows through the btree that leads with `tenant_id` (tenant document sets are small); `SET LOCAL hnsw.iterative_scan` inside `tenant_transaction`; per-tenant caps on candidates; and an eval with one very large tenant next to a small one.
- Check `EXPLAIN` as `app_ai` (not as the owner or a superuser) for the GIN `@@` path on `tsv` as well as the vector path.
- Never fix RLS performance with SECURITY DEFINER functions or BYPASSRLS.
- The LangGraph checkpoint tables (agents) are tenant data: they need `tenant_id` and RLS like `ai.chunks`.

## Corpus parsing

- Extraction: PDFium (`pypdfium2`) reads the PDFs' text layer and the standard library's `html.parser` reads the AnvisaLegis page. No OCR, no layout model. The whole corpus parses in under 2 seconds (the 732-page FDA PDF in 1.1 s) and the dependency adds about 10 MB.
- Why not Docling, the original plan: Docling 2.150.0 was measured on the FDA PDF on a CPU-only developer machine (20 threads). One conversion took 1,214 s, about 20 minutes, for the 732 pages, with OCR and table structure off, models already downloaded and about 2.4 GB of RAM in use. Its environment is about 1 GB (torch 2.15 alone is over 450 MB), and the layout model weights come from Hugging Face on first use. That is far too slow for tests that parse the corpus on every CI run. The three PDFs have a clean text layer, and every cut follows numbering printed in the text, so a layout model adds nothing the parser uses. Reconsider Docling for tenant uploads (scans, tables) in phase 2.
- How each source is cut:
  - FDA Food Code: only pages whose running header reads `FDA Food Code 2026 Chapter 1` to `8`. A line `N-NNN.NN Title` starts a section (part 4-10 has `4-1001.11`); chapter, part and subpart headings close it. The PDF is set without hyphenation, so a hyphen at a line end (U+FFFE from PDFium) stays a hyphen ("self-service").
  - Regulation 852/2004: page headers, consolidation markers (`►M2 ... ◄`, `▼C1`) and footnotes (a marker line, then `(1) ...` to the end of the page) are removed. `Article N` / `Artigo N.o` starts an article; inside an annex, `PART X:` and `CHAPTER N` start a section. Paths use English labels in both languages.
  - RDC 216/2004: the page declares UTF-8 but is Latin-1. The `<p>` paragraphs from `ANEXO` to `PUB D.O.U.` are kept, minus the site's "Redações Anteriores" links; a numbered paragraph starts an item. The annex numbers its items 1 to 4.12 (the phase 1 design says "1 to 5").
  - A section whose text is only an uppercase heading (RDC `4.1 EDIFICAÇÃO, ...`, EU `ANNEX I PRIMARY PRODUCTION`) is dropped.
- Known noise, kept as extracted or as published: some FDA sections end with the subheading of the next group (5-402.11 ends with "Location and placement"), and the margin labels of the date-marking figure land at the end of 3-501.16 and inside 3-501.17 ("Prepare and hold cold"); FDA defined terms keep their `*`; an EU compound broken at a line end loses its hyphen ("corrosionresistant"); RDC words the page itself runs together stay so ("higiênicosanitária").
- On the pinned files: FDA 366 sections, 852/2004 35 per language, RDC 128. `tests/test_corpus.py` parses the real files: without them it is skipped locally and fails in CI (`CI` set). CI fetches them with `corpus/scripts/fetch_corpus.py`, cached by manifest hash.

## Migrations

- Layout: `migrations/schema/` holds structure (tables, indexes, constraints, RLS policies, grants and revokes) and `migrations/data/` holds rows that every environment needs. Both are version locations of one linear Alembic history (`version_locations` in `alembic.ini`, newline separated). A revision is either schema or data, never both. Demo data is not a migration.
- Create: `uv run alembic revision --version-path migrations/schema -m "..."` for structure, `--version-path migrations/data` for rows. Ruff post-write hooks sort imports and format the new file; drop the unused `sqlalchemy` import when the revision only runs SQL.
- Write revisions as raw SQL with `op.execute(...)`, one statement per call, with a `downgrade()` that undoes the structure. There is no `--autogenerate`: policies and grants do not autogenerate, so `target_metadata` stays `None`. Qualify pgvector objects with `public.` (the extension lives there).
- Apply: `uv run alembic upgrade head` with `NORMETRY_MIGRATIONS_DATABASE_URL` (owner role, `postgresql+psycopg://...`) set in the shell for that command only. In the AppHost, the short-lived `ai-migrate` resource runs this command with only that variable, and the `ai` service starts after it exits 0.
- `FORCE ROW LEVEL SECURITY` binds `ai_owner` too: with no policy for it, the owner reads and writes no rows of `ai.chunks`.

## LLM safety

- Document text enters prompts delimited as data, never as instructions. Agents have no write tools; every state change goes through human review.
- Answers are structured: `answer` plus `claims`, each with the chunk ids that support it. Drop any claim citing an id that was not retrieved. Below the re-ranker threshold, refuse instead of guessing.
- Mask personal names in logs and traces. Never log secrets or full documents.
- A change to prompts, chunking, retrieval, embeddings or agents runs the evals (`eval-runner` agent) and reports the deltas.

## Tooling

From `src/ai`:

- `uv sync` · `uv run pytest` · `uv run ruff check` · `uv run ruff format` (`--check` in CI). The hook runs Ruff format on every edited `.py` file once `src/ai/.venv` exists.
- Run: `uv run uvicorn normetry_ai.api:create_app --factory` with `NORMETRY_INTERNAL_TOKEN` set. The AppHost (`dotnet run --project src/core/Normetry.AppHost` from the repo root) runs `uv sync`, waits for `ai-migrate`, then runs this command with `--reload` and sets `NORMETRY_INTERNAL_TOKEN`, `NORMETRY_DATABASE_URL` (`app_ai`) and the `OTEL_*` variables; environment variables take precedence over `src/ai/.env`.
- API tests stay offline: build the app with `create_app(Settings(..., _env_file=None))` and use FastAPI's `TestClient`.
- Database tests (`tests/test_database.py`, marker `integration`) start one `pgvector/pgvector:0.8.7-pg17` container per session with Testcontainers. They run the real bootstrap from `deploy/postgres/init/` with random passwords, then `alembic upgrade head` as `ai_owner`, and assert app behavior connected as the real roles, never as the superuser. They need Docker: without it they are skipped locally (or deselect them with `-m "not integration"`), and in CI (`CI` set) they fail instead of skipping. A new tenant-owned table needs its isolation tests there.
- Type hints on all public functions.
