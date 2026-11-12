using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normetry.Migrations.Schema
{
    /// <summary>
    /// Creates core.chat_message_counts, the daily chat message count per tenant, user and UTC day,
    /// with RLS forced and one policy for app_core (the rls-table skill).
    /// </summary>
    public partial class CreateChatMessageCounts : Migration
    {
        // Same expression as the first migration: NULL with no tenant set, evaluated once per
        // statement.
        private const string CurrentTenant =
            "(SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_message_counts",
                schema: "core",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_message_counts", x => new
                    {
                        x.tenant_id,
                        x.user_id,
                        x.day,
                    });
                    table.ForeignKey(
                        name: "fk_chat_message_counts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "core",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.Sql("ALTER TABLE core.chat_message_counts ENABLE ROW LEVEL SECURITY");
            migrationBuilder.Sql("ALTER TABLE core.chat_message_counts FORCE ROW LEVEL SECURITY");

            // Every command sees and writes only the current tenant's counts, and an update cannot
            // move a count to another tenant.
            migrationBuilder.Sql(
                $"""
                CREATE POLICY chat_message_counts_tenant ON core.chat_message_counts
                    FOR ALL TO app_core
                    USING (tenant_id = {CurrentTenant})
                    WITH CHECK (tenant_id = {CurrentTenant})
                """
            );
            // The app inserts and increments counts (an upsert needs SELECT, INSERT and UPDATE)
            // but never removes one, so DELETE from the default privileges goes.
            migrationBuilder.Sql("REVOKE DELETE ON core.chat_message_counts FROM app_core");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the table drops its policy and grants.
            migrationBuilder.DropTable(name: "chat_message_counts", schema: "core");
        }
    }
}
