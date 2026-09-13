using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Folio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class P10ShelvingSeriesNameAndWordCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeriesName",
                table: "Novels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ShelvedAtUtc",
                table: "Novels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WordCount",
                table: "Novels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeriesName",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "ShelvedAtUtc",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "WordCount",
                table: "Novels");
        }
    }
}
