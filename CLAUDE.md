# Normetry

Multi-tenant compliance copilot for regulated establishments (food service first: FDA Food Code, EU Regulation 852/2004, Anvisa RDC 216/2004). Public portfolio repository.

- Product, architecture, ADR summary and roadmap: `README.md`. Full decisions: `docs/adr/`.
- Status: phase 0 (foundation) is done; phase 1 (regulation RAG) is next. The .NET solution and `src/ai` are scaffolded; `src/web` is not yet. Update the commands below when they become real.

## Layout (monorepo)

| Path | What | Rules |
|---|---|---|
| `src/core/` | .NET 10 core: API, domain, EF Core, outbox, YARP, SignalR, Aspire AppHost | `src/core/CLAUDE.md` |
| `src/ai/` | Python 3.12+ AI service: FastAPI, ingestion, retrieval, LangGraph agents | `src/ai/CLAUDE.md` |
| `src/web/` | React + TypeScript (not started) | add `src/web/CLAUDE.md` when scaffolded |
| `src/shared/contracts/` | contracts both backends implement (`internal-http.md`: gateway headers) | change both sides in the same PR |
| `evals/` | eval datasets, synthetic establishments, reports | |
| `corpus/` | versioned regulation texts + download scripts | |
| `docs/adr/` | architecture decision records | `adr` skill |
| `docs/design/` | approved UI reference (`reference.png`) + design system (`DESIGN.md`) | `normetry-design-system` skill |
| `deploy/` | PostgreSQL bootstrap (`postgres/init/`), Keycloak dev realm; later docker-compose, Terraform | READMEs in `deploy/postgres/`, `deploy/keycloak/` |

## Invariants (changing one needs a new ADR)

- .NET writes only schema `core`; Python writes only schema `ai`. Results cross services as queue events.
- Tenant isolation is PostgreSQL RLS with the tenant set per transaction. Every tenant-owned table follows the `rls-table` skill.
- The tenant id comes from the validated JWT, never from the request body, query string or client headers.
- The Python service is never public: only YARP (`/ai/*`) reaches it, with an internal token.
- Nothing secret in the repo. Local: `.env` (git-ignored) and `dotnet user-secrets`. CI and deploy: GitHub Actions secrets. Never read, print or write secret values.

## Database migrations

- Each backend keeps its migrations in a separate area with two folders:
  - `schema`: structure only (tables, columns, indexes, constraints, RLS policies, grants and revokes);
  - `data`: rows a migration inserts, updates or deletes (reference data every environment needs).
- A migration is either schema or data, never both. Demo data for local development is not a migration: it goes through a development-only seed step.
- .NET: project `src/core/Normetry.Migrations` with `Schema/` and `Data/` folders (one EF Core history). It is a short-lived console app the AppHost runs as `core_owner` before the API starts (#12).
- Python: `src/ai/migrations/schema/` and `src/ai/migrations/data/`, two Alembic version locations in one history; create with `uv run alembic revision --version-path migrations/schema -m "..."` (or `migrations/data`). A short-lived AppHost step runs `alembic upgrade head` as `ai_owner` before the AI service starts (#13).
- Owner credentials exist only in those migration steps, never in a running service.

## Commands

- Run everything: `dotnet run --project src/core/Normetry.AppHost` (needs Docker; starts PostgreSQL, Keycloak, the API and the AI service; Aspire dashboard at `https://localhost:17081`, the login link is printed in the console). Demo users: `deploy/keycloak/README.md`
- .NET (from the repo root): `dotnet build` · `dotnet test` · `dotnet tool restore` then `dotnet csharpier check .`
- Python (from `src/ai`): `uv sync` · `uv run pytest` · `uv run ruff check` · `uv run ruff format --check` · run with `uv run uvicorn normetry_ai.api:create_app --factory` (needs `NORMETRY_INTERNAL_TOKEN`, see `src/ai/.env.example`)
- Claude hooks self-test: `node --test ".claude/hooks/*.test.mjs"`

## Workflow

- The user drives feature by feature. One feature = one GitHub issue → branch `feat/<issue>-<slug>` (or `fix/`, `docs/`, `chore/`) → PR containing `Closes #<issue>`. Use `gh` for issues and PRs.
- Issue lifecycle, every time:
  1. Starting an issue: `gh issue edit <n> --add-assignee @me --add-label "status: in progress"`, then create its branch.
  2. The PR body contains `Closes #<n>`, so the merge closes the issue. Merge with `gh pr merge <n> --rebase`: rebase is the only merge method enabled, so every atomic commit lands on `main` and release-please builds the changelog from them (a merge commit would repeat the PR title as an extra entry).
  3. After the PR merges into `main`: `gh issue edit <n> --remove-label "status: in progress"` and comment on the issue with the PR link and a short summary of what shipped (`gh issue comment <n> --body-file ...`).
- Conventional Commits, atomic: one logical change per commit. Commit or push only when the user asks.
- No AI attribution anywhere: no `Co-Authored-By` trailers, no "Generated with Claude Code" or similar lines, in commits, PR descriptions, issues, comments or release notes.
- Every branch merged into `main` is deleted. GitHub deletes the remote branch on merge; locally run `git switch main && git pull --prune` and, once the PR shows as merged, `git branch -D <branch>` (rebase gives the commits new SHAs, so `-d` refuses).
- Versioning is SemVer (`vMAJOR.MINOR.PATCH`), one version for the whole monorepo, `0.x` until the MVP is published (`1.0.0` = end of phase 3). release-please (`.github/workflows/release-please.yml`) derives it from the commits: `fix` → patch, `feat` → minor, `!` or `BREAKING CHANGE` → major (minor while `0.x`); `docs`/`chore`/`refactor`/`test`/`ci` do not release. It keeps a release PR open; merging that PR creates the tag, the GitHub Release and the `CHANGELOG.md` entry. Never create tags, bump versions or edit `CHANGELOG.md` by hand.
- Code, comments, docs, commits and PRs in English.
- Done = the affected builds and tests ran and passed. Prompt, retrieval or agent changes also need evals. Frontend changes also need a `ux-ui-designer` review.

## Agents and skills (`.claude/`)

| Use | When |
|---|---|
| `dotnet-core-engineer` agent | implementing anything in `src/core` |
| `ai-engineer` agent | implementing anything in `src/ai` |
| `web-engineer` agent | implementing anything in `src/web` (first version; refine after the web scaffold) |
| `ux-ui-designer` agent | **every** frontend change: screen spec before building, read-only design review after |
| `tenant-isolation-reviewer` agent (read-only) | after changes to persistence, SQL/RLS, auth, YARP, queue consumers, caching or search |
| `eval-runner` agent | after prompt, retrieval or agent changes; returns only metrics and regressions |
| `rls-table` skill | new or changed tenant-owned table or policy |
| `cqrs-slice` skill | new command, query or endpoint in `src/core` |
| `adr` skill | a significant decision is made or reversed |
| `humanizer` skill | before finishing README, docs, ADRs or PR descriptions: removes AI-writing tells (file mode keeps code, paths and links intact) |
| `normetry-design-system` skill | any UI work; the look is fixed by `docs/design/reference.png` + `docs/design/DESIGN.md` |
| `supabase-postgres-best-practices` skill | SQL, schema, indexes, pgvector, connections (ignore Supabase-only APIs) |

MCP servers (`.mcp.json`): `context7` for current library docs (Aspire, EF Core, LangGraph, pgvector, FastAPI) and `microsoft-learn` for .NET and Azure docs. Check docs before using a fast-moving API from memory. Never put secrets or private code in queries.

Hooks (`.claude/settings.json`): block secret-looking content in edits, secret-file reads, force pushes, broad recursive deletes and `terraform apply/destroy`; auto-format edited files once CSharpier, Ruff or Prettier is installed in the repo.
