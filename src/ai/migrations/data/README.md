# Data migrations

Revisions here insert, update or delete rows that every environment needs (reference data). They
share one linear history with `../schema`, so a data revision can depend on the table it fills.

- Create one with `uv run alembic revision --version-path migrations/data -m "..."`.
- A revision is either schema or data, never both: tables, indexes, policies and grants go in
  `../schema`.
- Demo data for local development is not a migration.
- Migrations run as `ai_owner`, and `FORCE ROW LEVEL SECURITY` binds the owner too. With no policy
  for `ai_owner`, it cannot write `ai.chunks`: the public corpus comes from the corpus ingestion
  job (`corpus_ingestor`), never from a data migration.

None exist yet.
