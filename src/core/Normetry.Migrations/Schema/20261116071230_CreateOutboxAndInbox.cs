using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normetry.Migrations.Schema
{
    /// <summary>
    /// The transactional outbox (core.outbox_messages), the inbox of consumed events
    /// (core.inbox_messages), both tenant-owned with RLS forced and one policy for app_core, and
    /// core.outbox_tenants: the tenants the background services visit, since app_core cannot list
    /// tenants. Its rows hold only a tenant id, which every app_core session may read; a session
    /// inserts only its own tenant's.
    /// </summary>
    public partial class CreateOutboxAndInbox : Migration
    {
        private const string CurrentTenant =
            "(SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "core",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    received_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.tenant_id, x.message_id });
                }
            );

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "core",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    routing_key = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    body = table.Column<string>(type: "json", nullable: false),
                    trace_parent = table.Column<string>(
                        type: "character varying(55)",
                        maxLength: 55,
                        nullable: true
                    ),
                    trace_state = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    sent_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_outbox_messages_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "core",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "outbox_tenants",
                schema: "core",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_tenants", x => x.tenant_id);
                    table.ForeignKey(
                        name: "fk_outbox_tenants_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "core",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id_created_at",
                schema: "core",
                table: "outbox_messages",
                columns: new[] { "tenant_id", "created_at" },
                filter: "sent_at IS NULL"
            );

            foreach (var table in new[] { "outbox_messages", "inbox_messages", "outbox_tenants" })
            {
                migrationBuilder.Sql($"ALTER TABLE core.{table} ENABLE ROW LEVEL SECURITY");
                migrationBuilder.Sql($"ALTER TABLE core.{table} FORCE ROW LEVEL SECURITY");
            }
            foreach (var table in new[] { "outbox_messages", "inbox_messages" })
            {
                migrationBuilder.Sql(
                    $"""
                    CREATE POLICY {table}_tenant ON core.{table} FOR ALL TO app_core
                        USING (tenant_id = {CurrentTenant})
                        WITH CHECK (tenant_id = {CurrentTenant})
                    """
                );
            }
            migrationBuilder.Sql(
                "CREATE POLICY outbox_tenants_select ON core.outbox_tenants FOR SELECT TO app_core USING (true)"
            );
            migrationBuilder.Sql(
                $"""
                CREATE POLICY outbox_tenants_insert ON core.outbox_tenants FOR INSERT TO app_core
                    WITH CHECK (tenant_id = {CurrentTenant})
                """
            );

            // Outbox rows are marked sent, never deleted; consumed messages and listed tenants are
            // only added.
            migrationBuilder.Sql("REVOKE DELETE ON core.outbox_messages FROM app_core");
            migrationBuilder.Sql(
                "REVOKE UPDATE, DELETE ON core.inbox_messages, core.outbox_tenants FROM app_core"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the tables drops their policies and grants.
            migrationBuilder.DropTable(name: "inbox_messages", schema: "core");

            migrationBuilder.DropTable(name: "outbox_messages", schema: "core");

            migrationBuilder.DropTable(name: "outbox_tenants", schema: "core");
        }
    }
}
