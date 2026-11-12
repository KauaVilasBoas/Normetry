# PostgreSQL bootstrap

`init/` runs once, through the image's initdb hook, when the data directory is empty. Mount or copy
it to `/docker-entrypoint-initdb.d` on the `pgvector/pgvector:0.8.7-pg17` image. The Aspire AppHost
does this with `WithInitFiles`; Testcontainers (#12, #13) and a future docker-compose file reuse the
same script.

## What it creates

In database `normetry`:

| Role | Purpose | Privileges |
|---|---|---|
| `core_owner` | EF Core migrations | owns schema `core`; nothing on `ai` |
| `ai_owner` | Alembic migrations | owns schema `ai`; nothing on `core` |
| `app_core` | .NET API at runtime | `USAGE` on `core`; DML on tables and `USAGE` on sequences that `core_owner` creates (default privileges) |
| `app_ai` | AI service at runtime | `USAGE` on `ai`; DML on tables and `USAGE` on sequences that `ai_owner` creates |
| `corpus_ingestor` | public corpus ingestion | `USAGE` on `ai` only; the ai migrations grant it the corpus tables explicitly |

Every role is `LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB`. The script also creates the
`vector` extension, revokes `CREATE` on schema `public` and `TEMPORARY` on the database from `PUBLIC`.

Owner credentials never go to a running service: owners can switch RLS off, so only the short-lived
migration steps use them. Default privileges also reach the migration history tables
(`core."__EFMigrationsHistory"`, `ai.alembic_version`); the first migration on each side revokes the app
roles' access to them.

## Environment variables

The container needs all of these; the script fails if any password is missing.

| Variable | Value |
|---|---|
| `POSTGRES_PASSWORD` | superuser password (required by the image) |
| `POSTGRES_DB` | `normetry` |
| `CORE_OWNER_PASSWORD`, `AI_OWNER_PASSWORD` | owner role passwords |
| `APP_CORE_PASSWORD`, `APP_AI_PASSWORD`, `CORPUS_INGESTOR_PASSWORD` | app role passwords |

Passwords are read inside psql with `\getenv`, so they never appear in a file or on a command line.
In tests, generate random values per run.

## Local data

The AppHost generates every password on the first run and stores it in its user secrets, and the
data lives in a Docker volume named `normetry.apphost-<hash>-postgres-data`. The script never runs
again on an existing volume, so after clearing the AppHost user secrets (or to apply a change to
`init/`), delete that volume too: `docker volume rm <name>`.
