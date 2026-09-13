using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Folio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase5SeriesAwareness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreviousBookId",
                table: "Novels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SeriesId",
                table: "Novels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesSequence",
                table: "Novels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SeriesContinuityJson",
                table: "BookAnalyses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Novels_SeriesId_SeriesSequence",
                table: "Novels",
                columns: new[] { "SeriesId", "SeriesSequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Novels_SeriesId_SeriesSequence",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "PreviousBookId",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "SeriesSequence",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "SeriesContinuityJson",
                table: "BookAnalyses");
        }
    }
}
