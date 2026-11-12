# Data migrations

Migrations here insert, update or delete rows that every environment needs (reference data). They
share one EF Core history and one model snapshot with `../Schema`, ordered by their timestamp, so a
data migration can fill a table that an earlier schema migration created.

- Create one with
  `dotnet ef migrations add <Name> --project src/core/Normetry.Migrations --output-dir Data`, then
  write the rows in `Up` (`migrationBuilder.InsertData` or `Sql`) and remove them in `Down`.
- A migration is either schema or data, never both. If EF Core scaffolds tables or columns into a
  data migration, the model has changed: remove it, add a schema migration first, then try again.
- Demo data for local development is not a migration. The API seeds the two demo tenants itself,
  in Development only (`DemoTenantSeed` in `Normetry.Infrastructure`).
- Migrations run as `core_owner`, and `FORCE ROW LEVEL SECURITY` binds the owner too. With no
  policy for `core_owner`, a data migration cannot write tenant-owned tables such as
  `core.tenants` or `core.establishments`. Reference data goes only in tables without RLS.

None exist yet.
