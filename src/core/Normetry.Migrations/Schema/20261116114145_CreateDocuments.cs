using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normetry.Migrations.Schema
{
    /// <summary>
    /// The tenant-owned core.documents and core.document_versions, with RLS forced and one policy
    /// for app_core each (the rls-table skill). Keys are (tenant_id, id), and a document
    /// references its establishment by (tenant_id, id) too, so no foreign key joins two tenants.
    /// Documents are soft-deleted and versions only change status: app_core gets no DELETE.
    /// </summary>
    public partial class CreateDocuments : Migration
    {
        private const string CurrentTenant =
            "(SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_establishments_tenant_id_id",
                schema: "core",
                table: "establishments",
                columns: new[] { "tenant_id", "id" }
            );

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    type = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    deleted_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => new { x.tenant_id, x.id });
                    table.CheckConstraint(
                        "ck_documents_type",
                        "type IN ('Sop', 'License', 'Log', 'Report', 'Other')"
                    );
                    table.ForeignKey(
                        name: "fk_documents_establishments_tenant_id_establishment_id",
                        columns: x => new { x.tenant_id, x.establishment_id },
                        principalSchema: "core",
                        principalTable: "establishments",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "document_versions",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    content_type = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    blob_path = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: false
                    ),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    status_changed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    failure_code = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: true
                    ),
                    failure_reason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    chunk_count = table.Column<int>(type: "integer", nullable: true),
                    page_count = table.Column<int>(type: "integer", nullable: true),
                    language = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_versions", x => new { x.tenant_id, x.id });
                    table.CheckConstraint(
                        "ck_document_versions_status",
                        "status IN ('Uploaded', 'Processing', 'Indexed', 'Failed')"
                    );
                    table.ForeignKey(
                        name: "fk_document_versions_documents_tenant_id_document_id",
                        columns: x => new { x.tenant_id, x.document_id },
                        principalSchema: "core",
                        principalTable: "documents",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "fk_document_versions_users_tenant_id_uploaded_by",
                        columns: x => new { x.tenant_id, x.uploaded_by },
                        principalSchema: "core",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_tenant_id_document_id_number",
                schema: "core",
                table: "document_versions",
                columns: new[] { "tenant_id", "document_id", "number" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_tenant_id_status_changed_at",
                schema: "core",
                table: "document_versions",
                columns: new[] { "tenant_id", "status_changed_at" },
                filter: "status = 'Processing'"
            );

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_tenant_id_uploaded_by",
                schema: "core",
                table: "document_versions",
                columns: new[] { "tenant_id", "uploaded_by" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_documents_tenant_id_establishment_id",
                schema: "core",
                table: "documents",
                columns: new[] { "tenant_id", "establishment_id" }
            );

            foreach (var table in new[] { "documents", "document_versions" })
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
            migrationBuilder.Sql(
                "REVOKE DELETE ON core.documents, core.document_versions FROM app_core"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the tables drops their policies and grants.
            migrationBuilder.DropTable(name: "document_versions", schema: "core");

            migrationBuilder.DropTable(name: "documents", schema: "core");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_establishments_tenant_id_id",
                schema: "core",
                table: "establishments"
            );
        }
    }
}
