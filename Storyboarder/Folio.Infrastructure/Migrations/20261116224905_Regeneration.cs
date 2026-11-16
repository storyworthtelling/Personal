using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Folio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Regeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RegeneratedAtUtc",
                table: "Novels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Regenerating",
                table: "Novels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RegenerationRank",
                table: "Novels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RegenerationReset",
                table: "Novels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RegeneratedAtUtc",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "Regenerating",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "RegenerationRank",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "RegenerationReset",
                table: "Novels");
        }
    }
}
