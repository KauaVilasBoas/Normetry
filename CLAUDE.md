# Normetry

Multi-tenant compliance copilot for regulated establishments (food service first: FDA Food Code, EU Regulation 852/2004, Anvisa RDC 216/2004). Public portfolio repository.

- Product, architecture, ADR summary and roadmap: `README.md`. Full decisions: `docs/adr/`.
- Status: phase 0 (foundation). Nothing is scaffolded yet: the commands below are the planned ones. Update this file when they become real.

## Layout (monorepo)

| Path | What | Rules |
|---|---|---|
| `src/core/` | .NET 10 core: API, domain, EF Core, outbox, YARP, SignalR, Aspire AppHost | `src/core/CLAUDE.md` |
| `src/ai/` | Python 3.12+ AI service: FastAPI, ingestion, retrieval, LangGraph agents | `src/ai/CLAUDE.md` |
| `src/web/` | React + TypeScript (not started) | add `src/web/CLAUDE.md` when scaffolded |
| `evals/` | eval datasets, synthetic establishments, reports | |
| `corpus/` | versioned regulation texts + download scripts | |
| `docs/adr/` | architecture decision records | `adr` skill |
| `docs/design/` | approved UI reference (`reference.png`) + design system (`DESIGN.md`) | `normetry-design-system` skill |
| `deploy/` | docker-compose, Terraform | |

## Invariants (changing one needs a new ADR)

- .NET writes only schema `core`; Python writes only schema `ai`. Results cross services as queue events.
- Tenant isolation is PostgreSQL RLS with the tenant set per transaction. Every tenant-owned table follows the `rls-table` skill.
- The tenant id comes from the validated JWT, never from the request body, query string or client headers.
- The Python service is never public: only YARP (`/ai/*`) reaches it, with an internal token.
- Nothing secret in the repo. Local: `.env` (git-ignored) and `dotnet user-secrets`. CI and deploy: GitHub Actions secrets. Never read, print or write secret values.

## Commands (planned)

- Run everything: `dotnet run --project src/core/Normetry.AppHost`
- .NET: `dotnet build` · `dotnet test`
- Python (from `src/ai`): `uv sync` · `uv run pytest` · `uv run ruff check`
- Claude hooks self-test: `node --test ".claude/hooks/*.test.mjs"`

## Workflow

- The user drives feature by feature. One feature = one GitHub issue → branch `feat/<issue>-<slug>` (or `fix/`, `docs/`, `chore/`) → PR containing `Closes #<issue>`. Use `gh` for issues and PRs.
- Conventional Commits, atomic: one logical change per commit. Commit or push only when the user asks.
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
| `normetry-design-system` skill | any UI work; the look is fixed by `docs/design/reference.png` + `docs/design/DESIGN.md` |
| `supabase-postgres-best-practices` skill | SQL, schema, indexes, pgvector, connections (ignore Supabase-only APIs) |

MCP servers (`.mcp.json`): `context7` for current library docs (Aspire, EF Core, LangGraph, pgvector, FastAPI) and `microsoft-learn` for .NET and Azure docs. Check docs before using a fast-moving API from memory. Never put secrets or private code in queries.

Hooks (`.claude/settings.json`): block secret-looking content in edits, secret-file reads, force pushes, broad recursive deletes and `terraform apply/destroy`; auto-format edited files once CSharpier, Ruff or Prettier is installed in the repo.
