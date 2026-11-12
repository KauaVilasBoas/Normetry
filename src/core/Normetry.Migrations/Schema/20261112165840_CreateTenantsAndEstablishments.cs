using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normetry.Migrations.Schema
{
    /// <summary>
    /// Locks the migration history away from app_core, then creates core.tenants and
    /// core.establishments with RLS forced and policies for app_core only (the rls-table skill).
    /// </summary>
    public partial class CreateTenantsAndEstablishments : Migration
    {
        // The tenant of the current transaction, NULL when none is set. The scalar subquery is
        // evaluated once per statement (an InitPlan), not once per row. With no tenant no row
        // matches, so nothing is visible and every write is rejected.
        private const string CurrentTenant =
            "(SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF Core creates core."__EFMigrationsHistory" as core_owner just before the first
            // migration, so the bootstrap's default privileges give app_core DML on it. The app
            // role never needs the history.
            migrationBuilder.Sql("""REVOKE ALL ON core."__EFMigrationsHistory" FROM app_core""");

            migrationBuilder.EnsureSchema(name: "core");

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "establishments",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    type = table.Column<string>(type: "text", nullable: false),
                    jurisdiction = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_establishments", x => x.id);
                    table.CheckConstraint(
                        "ck_establishments_jurisdiction",
                        "jurisdiction IN ('US', 'EU', 'BR')"
                    );
                    table.CheckConstraint(
                        "ck_establishments_type",
                        "type IN ('Restaurant', 'Pharmacy', 'Clinic')"
                    );
                    table.ForeignKey(
                        name: "fk_establishments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "core",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_establishments_tenant_id_name",
                schema: "core",
                table: "establishments",
                columns: new[] { "tenant_id", "name" }
            );

            // FORCE makes RLS bind the owner too: core_owner has no policy, so it sees no rows.
            foreach (var table in new[] { "tenants", "establishments" })
            {
                migrationBuilder.Sql($"ALTER TABLE core.{table} ENABLE ROW LEVEL SECURITY");
                migrationBuilder.Sql($"ALTER TABLE core.{table} FORCE ROW LEVEL SECURITY");
            }

            // A session reads only its own tenant row and may insert only that row (the
            // Development seed does). Tenants are never changed or removed through app_core, so
            // UPDATE and DELETE from the default privileges go.
            migrationBuilder.Sql(
                $"""
                CREATE POLICY tenants_select ON core.tenants FOR SELECT TO app_core
                    USING (id = {CurrentTenant})
                """
            );
            migrationBuilder.Sql(
                $"""
                CREATE POLICY tenants_insert ON core.tenants FOR INSERT TO app_core
                    WITH CHECK (id = {CurrentTenant})
                """
            );
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON core.tenants FROM app_core");

            // Every command sees and writes only the current tenant's rows, and an update cannot
            // move a row to another tenant.
            migrationBuilder.Sql(
                $"""
                CREATE POLICY establishments_tenant ON core.establishments FOR ALL TO app_core
                    USING (tenant_id = {CurrentTenant})
                    WITH CHECK (tenant_id = {CurrentTenant})
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the tables drops their policies and grants. The history revoke stays on
            // purpose: the table outlives a rollback, and app_core must never rewrite it.
            migrationBuilder.DropTable(name: "establishments", schema: "core");

            migrationBuilder.DropTable(name: "tenants", schema: "core");
        }
    }
}
