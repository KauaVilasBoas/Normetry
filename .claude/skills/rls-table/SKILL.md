---
name: rls-table
description: Normetry recipe for adding or changing a tenant-owned PostgreSQL table (schema core via EF Core, or schema ai via Alembic) with Row-Level Security, role grants and the mandatory cross-tenant isolation test. Use whenever a migration creates a table holding tenant data, adds a tenant_id column, or changes an RLS policy.
---

# Tenant-owned table with RLS

Isolation is enforced by PostgreSQL, not by `WHERE tenant_id = ...` in application code. A tenant-owned table is not done until its isolation test passes.

## 1. Table

- `tenant_id uuid NOT NULL`. Only `ai.chunks` allows `NULL`, meaning the public regulation corpus.
- Indexes used by tenant-scoped lookups lead with `tenant_id`.
- Ownership: `core.*` is written only by .NET (EF Core migrations); `ai.*` only by Python (Alembic).

## 2. Policies (raw SQL inside the migration)

```sql
ALTER TABLE core.<table> ENABLE ROW LEVEL SECURITY;
ALTER TABLE core.<table> FORCE ROW LEVEL SECURITY;  -- applies to the table owner too

CREATE POLICY <table>_tenant ON core.<table>
  USING      (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
  WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
```

- With no tenant set, `NULLIF(...)` is `NULL`: no rows are visible and writes are rejected (fail closed).
- EF Core: `migrationBuilder.Sql(...)` in `Up`, and the matching `DROP POLICY` / `DISABLE ROW LEVEL SECURITY` in `Down`.
- Alembic: `op.execute(...)` in `upgrade` / `downgrade`.

## 3. Grants

- The app role (`app_core` for core, `app_ai` for ai) gets only the privileges it needs.
- App roles never have `BYPASSRLS`, superuser or table ownership. Migrations run as a separate owner role.
- Rows with `tenant_id IS NULL` (public corpus) are writable only by `corpus_ingestor`.

## 4. Setting the tenant

- Per transaction: `SELECT set_config('app.tenant_id', @tenant, true)`. The `true` makes it transaction-local, so a pooled connection never carries a tenant into the next request.
- .NET: the EF Core interceptor sets it. Handlers never set it themselves.
- Python: the unit-of-work helper sets it at the start of each transaction. Workers take the tenant from the queue message.

## 5. Isolation test (required)

Integration test against a real PostgreSQL (Testcontainers), connected as the app role, not a superuser:

- tenant A inserts a row; with tenant B set, `SELECT` returns 0 rows and `UPDATE` / `DELETE` affect 0 rows;
- with tenant B set, an `INSERT` carrying tenant A's id fails;
- with no tenant set, `SELECT` returns 0 rows (only the public corpus for `ai.chunks`) and `INSERT` fails.

Policy performance and indexing: see the `supabase-postgres-best-practices` skill (`references/security-rls-*.md`). Ignore Supabase-only functions such as `auth.uid()`; Normetry uses `app.tenant_id`.
