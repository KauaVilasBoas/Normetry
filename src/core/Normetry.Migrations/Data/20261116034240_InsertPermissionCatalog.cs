using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normetry.Migrations.Data
{
    /// <summary>
    /// The initial permission catalog. The names are written out, not read from
    /// <c>Normetry.Domain.Identity.Permissions</c>: a migration is a fixed step, and a permission
    /// added later gets its own data migration.
    /// </summary>
    public partial class InsertPermissionCatalog : Migration
    {
        private static readonly string[] Names =
        [
            "establishments.read",
            "establishments.manage",
            "documents.read",
            "documents.upload",
            "documents.delete",
            "chat.ask",
            "profiles.read",
            "profiles.manage",
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
