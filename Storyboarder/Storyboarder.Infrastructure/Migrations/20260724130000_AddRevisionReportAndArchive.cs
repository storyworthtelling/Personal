using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionReportAndArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ChaptersDetected",
                table: "Novels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReportOnly",
                table: "Novels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ArchiveFolderId",
                table: "Novels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ArchivedAtUtc",
                table: "Novels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChapterNumber",
                table: "Chunks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ChapterLabel",
                table: "Chunks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ChunkMentions",
                table: "DesignRefs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsSheet",
                table: "DesignRefs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FactChecksJson",
                table: "BookAnalyses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedDriveFileId",
                table: "BookAnalyses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedDriveFileId",
                table: "Volumes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Chapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    StartOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    EndOffset = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chapters_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_NovelId_Number",
                table: "Chapters",
                columns: new[] { "NovelId", "Number" });

            // A new non-nullable bool lands as 0 (false) on every existing row, which for NeedsSheet
            // would silently skip every reference sheet an in-flight conversion still has queued. The
            // recurring-cast rule only applies to bibles consolidated from here on, so backfill the
            // existing rows to true — the state they were created in before this column existed.
            migrationBuilder.Sql("UPDATE DesignRefs SET NeedsSheet = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Chapters");

            migrationBuilder.DropColumn(
                name: "ChaptersDetected",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "ReportOnly",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "ArchiveFolderId",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "ChapterNumber",
                table: "Chunks");

            migrationBuilder.DropColumn(
                name: "ChapterLabel",
                table: "Chunks");

            migrationBuilder.DropColumn(
                name: "ChunkMentions",
                table: "DesignRefs");

            migrationBuilder.DropColumn(
                name: "NeedsSheet",
                table: "DesignRefs");

            migrationBuilder.DropColumn(
                name: "FactChecksJson",
                table: "BookAnalyses");

            migrationBuilder.DropColumn(
                name: "ArchivedDriveFileId",
                table: "BookAnalyses");

            migrationBuilder.DropColumn(
                name: "ArchivedDriveFileId",
                table: "Volumes");
        }
    }
}
