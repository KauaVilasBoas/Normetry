using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normetry.Migrations.Data
{
    /// <summary>
    /// The inspection permissions. Owner gets them through <c>grants_all</c>; new tenants' Manager
    /// and Auditor get their defaults when they are created, which no migration can do (RLS keeps
    /// <c>core_owner</c> out of tenant tables).
    /// </summary>
    public partial class AddInspectionPermissions : Migration
    {
        private static readonly string[] Names =
        [
            "inspections.read",
            "inspections.run",
            "inspections.review",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permissions",
                schema: "core",
                column: "name",
                values: Names
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "permissions",
                schema: "core",
                keyColumn: "name",
                keyValues: Names
            );
        }
    }
}
