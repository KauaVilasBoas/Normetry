#!/usr/bin/env bash
# Database bootstrap, run once by the postgres image's initdb hook (/docker-entrypoint-initdb.d) on an
# empty data directory. Passwords come only from the environment: see deploy/postgres/README.md.
# The entrypoint sources this file when it is not executable, so it must work both ways.
set -e

: "${POSTGRES_DB:?must be set (normetry)}"
: "${CORE_OWNER_PASSWORD:?must be set}"
: "${AI_OWNER_PASSWORD:?must be set}"
: "${APP_CORE_PASSWORD:?must be set}"
: "${APP_AI_PASSWORD:?must be set}"
: "${CORPUS_INGESTOR_PASSWORD:?must be set}"

# \getenv reads the passwords inside psql: they never appear on a command line or in this file.
psql -v ON_ERROR_STOP=1 --no-psqlrc --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<'SQL'
\getenv core_owner_password CORE_OWNER_PASSWORD
\getenv ai_owner_password AI_OWNER_PASSWORD
\getenv app_core_password APP_CORE_PASSWORD
\getenv app_ai_password APP_AI_PASSWORD
\getenv corpus_ingestor_password CORPUS_INGESTOR_PASSWORD

-- No role is a superuser, bypasses RLS, or creates roles or databases.
CREATE ROLE core_owner LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB PASSWORD :'core_owner_password';
CREATE ROLE ai_owner LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB PASSWORD :'ai_owner_password';
CREATE ROLE app_core LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB PASSWORD :'app_core_password';
CREATE ROLE app_ai LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB PASSWORD :'app_ai_password';
CREATE ROLE corpus_ingestor LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB PASSWORD :'corpus_ingestor_password';

CREATE EXTENSION IF NOT EXISTS vector;
-- Objects live only in core and ai; nobody creates anything in public.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
-- No temp tables either: a pooled connection could otherwise carry one into another tenant's request.
REVOKE TEMPORARY ON DATABASE :"DBNAME" FROM PUBLIC;

-- Each owner owns one schema and has no privilege on the other. Migrations (EF Core for core,
-- Alembic for ai) run as the owner; ai must exist before Alembic writes ai.alembic_version.
CREATE SCHEMA core AUTHORIZATION core_owner;
CREATE SCHEMA ai AUTHORIZATION ai_owner;

-- App roles get DML on what the owner creates later, never ownership. RLS restricts the rows.
GRANT USAGE ON SCHEMA core TO app_core;
ALTER DEFAULT PRIVILEGES FOR ROLE core_owner IN SCHEMA core
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_core;
ALTER DEFAULT PRIVILEGES FOR ROLE core_owner IN SCHEMA core
  GRANT USAGE ON SEQUENCES TO app_core;

GRANT USAGE ON SCHEMA ai TO app_ai, corpus_ingestor;
ALTER DEFAULT PRIVILEGES FOR ROLE ai_owner IN SCHEMA ai
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_ai;
ALTER DEFAULT PRIVILEGES FOR ROLE ai_owner IN SCHEMA ai
  GRANT USAGE ON SEQUENCES TO app_ai;
-- corpus_ingestor gets no default grants: the ai migrations grant it only the corpus tables it writes.
SQL
