using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normetry.Migrations.Schema
{
    /// <summary>
    /// The tenant-owned core.inspections, core.findings, core.action_items and
    /// core.inspection_counts, with RLS forced and one policy for app_core each (the rls-table
    /// skill). Keys lead with tenant_id, and every reference is (tenant_id, id), so no foreign key
    /// joins two tenants. Rows only change status or review: app_core gets no DELETE.
    /// </summary>
    public partial class CreateInspections : Migration
    {
        private const string CurrentTenant =
            "(SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)";

        private static readonly string[] Tables =
        [
            "inspections",
            "findings",
            "action_items",
            "inspection_counts",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inspection_counts",
                schema: "core",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspection_counts", x => new { x.tenant_id, x.day });
                    table.ForeignKey(
                        name: "fk_inspection_counts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "core",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "inspections",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    requested_by = table.Column<string>(
                        type: "character varying(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    requested_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    started_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    completed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    items_total = table.Column<int>(type: "integer", nullable: false),
                    items_evaluated = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    cost_usd = table.Column<decimal>(
                        type: "numeric(12,6)",
                        precision: 12,
                        scale: 6,
                        nullable: true
                    ),
                    failure_reason_code = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: true
                    ),
                    failure_reason = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    themes = table.Column<string>(type: "jsonb", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspections", x => new { x.tenant_id, x.id });
                    table.CheckConstraint(
                        "ck_inspections_status",
                        "status IN ('Requested', 'Running', 'Completed', 'Failed')"
                    );
                    table.ForeignKey(
                        name: "fk_inspections_establishments_tenant_id_establishment_id",
                        columns: x => new { x.tenant_id, x.establishment_id },
                        principalSchema: "core",
                        principalTable: "establishments",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "findings",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inspection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    theme = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    title = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    severity = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    verdict = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: false
                    ),
                    confidence = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    rationale = table.Column<string>(
                        type: "character varying(4000)",
                        maxLength: 4000,
                        nullable: false
                    ),
                    review_status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    review_note = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: true
                    ),
                    evidence_citations = table.Column<string>(type: "jsonb", nullable: false),
                    original = table.Column<string>(type: "jsonb", nullable: true),
                    regulation_citations = table.Column<string>(type: "jsonb", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_findings", x => new { x.tenant_id, x.id });
                    table.CheckConstraint(
                        "ck_findings_confidence",
                        "confidence IN ('low', 'medium', 'high')"
                    );
                    table.CheckConstraint(
                        "ck_findings_review_status",
                        "review_status IN ('Pending', 'Accepted', 'Rejected', 'Edited')"
                    );
                    table.CheckConstraint(
                        "ck_findings_severity",
                        "severity IN ('critical', 'major', 'minor')"
                    );
                    table.CheckConstraint(
                        "ck_findings_verdict",
                        "verdict IN ('compliant', 'non_compliant', 'insufficient_evidence')"
                    );
                    table.ForeignKey(
                        name: "fk_findings_inspections_tenant_id_inspection_id",
                        columns: x => new { x.tenant_id, x.inspection_id },
                        principalSchema: "core",
                        principalTable: "inspections",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "fk_findings_users_tenant_id_reviewed_by",
                        columns: x => new { x.tenant_id, x.reviewed_by },
                        principalSchema: "core",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "action_items",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action_items", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_action_items_status", "status IN ('Open', 'Done')");
                    table.ForeignKey(
                        name: "fk_action_items_findings_tenant_id_finding_id",
                        columns: x => new { x.tenant_id, x.finding_id },
                        principalSchema: "core",
                        principalTable: "findings",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "fk_action_items_users_tenant_id_updated_by",
                        columns: x => new { x.tenant_id, x.updated_by },
                        principalSchema: "core",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_action_items_tenant_id_finding_id",
                schema: "core",
                table: "action_items",
                columns: new[] { "tenant_id", "finding_id" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "ix_action_items_tenant_id_updated_by",
                schema: "core",
                table: "action_items",
                columns: new[] { "tenant_id", "updated_by" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_findings_tenant_id_inspection_id",
                schema: "core",
                table: "findings",
                columns: new[] { "tenant_id", "inspection_id" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_findings_tenant_id_reviewed_by",
                schema: "core",
                table: "findings",
                columns: new[] { "tenant_id", "reviewed_by" }
            );

            migrationBuilder.CreateIndex(
                name: "ix_inspections_tenant_id_establishment_id",
                schema: "core",
                table: "inspections",
                columns: new[] { "tenant_id", "establishment_id" },
                unique: true,
                filter: "status IN ('Requested', 'Running')"
            );

            migrationBuilder.CreateIndex(
                name: "ix_inspections_tenant_id_establishment_id_requested_at",
                schema: "core",
                table: "inspections",
                columns: new[] { "tenant_id", "establishment_id", "requested_at" }
            );

            foreach (var table in Tables)
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
            // Inspections change status, findings their review and action items their status;
            // counts are incremented. Nothing is removed.
            migrationBuilder.Sql(
                """
                REVOKE DELETE ON core.inspections, core.findings, core.action_items,
                    core.inspection_counts FROM app_core
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the tables drops their policies and grants.
            migrationBuilder.DropTable(name: "action_items", schema: "core");

            migrationBuilder.DropTable(name: "inspection_counts", schema: "core");

            migrationBuilder.DropTable(name: "findings", schema: "core");

            migrationBuilder.DropTable(name: "inspections", schema: "core");
        }
    }
}
