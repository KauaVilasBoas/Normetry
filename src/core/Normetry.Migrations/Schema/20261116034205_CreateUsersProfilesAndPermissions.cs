using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normetry.Migrations.Schema
{
    /// <summary>
    /// Creates the permission catalog (core.permissions, global, no RLS) and the tenant-owned
    /// profiles, profile_permissions, users and user_profiles, with RLS forced and one policy for
    /// app_core each (the rls-table skill). The links reference (tenant_id, id), so a link cannot
    /// join rows of two tenants. System profiles are created at runtime, never here: RLS keeps
    /// core_owner out of tenant tables.
    /// </summary>
    public partial class CreateUsersProfilesAndPermissions : Migration
    {
        // Same expression as the first migration: NULL with no tenant set, evaluated once per
        // statement.
        private const string CurrentTenant =
            "(SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "core",
                columns: table => new
                {
                    name = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.name);
                }
            );

            migrationBuilder.CreateTable(
                name: "profiles",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    system_key = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: true
                    ),
                    grants_all = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profiles", x => new { x.tenant_id, x.id });
                    table.CheckConstraint(
                        "ck_profiles_grants_all",
                        "grants_all = (coalesce(system_key, '') = 'owner')"
                    );
                    table.CheckConstraint(
                        "ck_profiles_system_key",
                        "system_key IN ('owner', 'manager', 'auditor')"
                    );
                    table.ForeignKey(
                        name: "fk_profiles_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "core",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "users",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_users_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "core",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "profile_permissions",
                schema: "core",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profile_permissions", x => new
                    {
                        x.tenant_id,
                        x.profile_id,
                        x.permission,
                    });
                    table.ForeignKey(
                        name: "fk_profile_permissions_permissions_permission",
                        column: x => x.permission,
                        principalSchema: "core",
                        principalTable: "permissions",
                        principalColumn: "name",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "fk_profile_permissions_profiles_tenant_id_profile_id",
                        columns: x => new { x.tenant_id, x.profile_id },
                        principalSchema: "core",
                        principalTable: "profiles",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "user_profiles",
                schema: "core",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_profiles", x => new
                    {
                        x.tenant_id,
                        x.user_id,
                        x.profile_id,
                    });
                    table.ForeignKey(
                        name: "fk_user_profiles_profiles_tenant_id_profile_id",
                        columns: x => new { x.tenant_id, x.profile_id },
                        principalSchema: "core",
                        principalTable: "profiles",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "fk_user_profiles_users_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "core",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_profile_permissions_permission",
                schema: "core",
                table: "profile_permissions",
                column: "permission"
            );

            migrationBuilder.CreateIndex(
                name: "ix_profiles_tenant_id_name",
                schema: "core",
                table: "profiles",
                columns: new[] { "tenant_id", "name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_profiles_tenant_id_system_key",
                schema: "core",
                table: "profiles",
                columns: new[] { "tenant_id", "system_key" },
                unique: true,
                filter: "system_key IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_tenant_id_profile_id",
                schema: "core",
                table: "user_profiles",
                columns: new[] { "tenant_id", "profile_id" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_sub",
                schema: "core",
                table: "users",
                columns: new[] { "tenant_id", "sub" },
                unique: true
            );

            // The catalog is global reference data with no tenant, so it has no RLS: app_core
            // only reads it, and only data migrations (as core_owner) write it.
            migrationBuilder.Sql("REVOKE INSERT, UPDATE, DELETE ON core.permissions FROM app_core");

            // FORCE makes RLS bind the owner too. Every command sees and writes only the current
            // tenant's rows, and an update cannot move a row to another tenant.
            foreach (
                var table in new[] { "profiles", "profile_permissions", "users", "user_profiles" }
            )
            {
                migrationBuilder.Sql($"ALTER TABLE core.{table} ENABLE ROW LEVEL SECURITY");
                migrationBuilder.Sql($"ALTER TABLE core.{table} FORCE ROW LEVEL SECURITY");
                migrationBuilder.Sql(
                    $"""
                    CREATE POLICY {table}_tenant ON core.{table} FOR ALL TO app_core
                        USING (tenant_id = {CurrentTenant})
                        WITH CHECK (tenant_id = {CurrentTenant})
                    """
                );
            }

            // Links are added and removed, never changed, and users are never deleted, so those
            // privileges from the default grants go.
            migrationBuilder.Sql(
                "REVOKE UPDATE ON core.profile_permissions, core.user_profiles FROM app_core"
            );
            migrationBuilder.Sql("REVOKE DELETE ON core.users FROM app_core");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the tables drops their policies and grants.
            migrationBuilder.DropTable(name: "profile_permissions", schema: "core");

            migrationBuilder.DropTable(name: "user_profiles", schema: "core");

            migrationBuilder.DropTable(name: "permissions", schema: "core");

            migrationBuilder.DropTable(name: "profiles", schema: "core");

            migrationBuilder.DropTable(name: "users", schema: "core");
        }
    }
}
