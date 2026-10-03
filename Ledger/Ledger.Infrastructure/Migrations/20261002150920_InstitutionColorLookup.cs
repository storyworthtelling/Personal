using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Index.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InstitutionColorLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ColorLookedUpAtUtc",
                table: "Institutions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaidInstitutionId",
                table: "Institutions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColorLookedUpAtUtc",
                table: "Institutions");

            migrationBuilder.DropColumn(
                name: "PlaidInstitutionId",
                table: "Institutions");
        }
    }
}
