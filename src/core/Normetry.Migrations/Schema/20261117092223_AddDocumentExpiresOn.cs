using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normetry.Migrations.Schema
{
    /// <summary>
    /// The optional expiry date of a document (a license, a certificate). The existing RLS policy
    /// and grants of core.documents cover the new column.
    /// </summary>
    public partial class AddDocumentExpiresOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "expires_on",
                schema: "core",
                table: "documents",
                type: "date",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "expires_on", schema: "core", table: "documents");
        }
    }
}
