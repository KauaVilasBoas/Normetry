using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normetry.Migrations.Schema
{
    /// <summary>
    /// Moves the one cross-tenant read, the list of tenants in core.outbox_tenants, from app_core
    /// to core_dispatcher, a role that reads that table and nothing else (the bootstrap creates it
    /// with USAGE on schema core and no default grants). app_core keeps INSERT for its own tenant's
    /// row, so no request path can list the tenants any more.
    /// </summary>
    public partial class RestrictTenantListToCoreDispatcher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY outbox_tenants_select ON core.outbox_tenants");
            migrationBuilder.Sql("REVOKE SELECT ON core.outbox_tenants FROM app_core");
            migrationBuilder.Sql("GRANT SELECT ON core.outbox_tenants TO core_dispatcher");
            migrationBuilder.Sql(
                "CREATE POLICY outbox_tenants_dispatcher ON core.outbox_tenants FOR SELECT TO core_dispatcher USING (true)"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY outbox_tenants_dispatcher ON core.outbox_tenants");
            migrationBuilder.Sql("REVOKE SELECT ON core.outbox_tenants FROM core_dispatcher");
            migrationBuilder.Sql("GRANT SELECT ON core.outbox_tenants TO app_core");
            migrationBuilder.Sql(
                "CREATE POLICY outbox_tenants_select ON core.outbox_tenants FOR SELECT TO app_core USING (true)"
            );
        }
    }
}
