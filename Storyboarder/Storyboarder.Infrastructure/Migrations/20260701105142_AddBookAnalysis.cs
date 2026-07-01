using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsolidatedJson = table.Column<string>(type: "TEXT", nullable: true),
                    PdfStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    PdfPath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookAnalyses_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChunkAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    NotesJson = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChunkAnalyses_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookAnalyses_NovelId",
                table: "BookAnalyses",
                column: "NovelId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChunkAnalyses_NovelId_Ordinal",
                table: "ChunkAnalyses",
                columns: new[] { "NovelId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_ChunkAnalyses_Status_NextAttemptAtUtc",
                table: "ChunkAnalyses",
                columns: new[] { "Status", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookAnalyses");

            migrationBuilder.DropTable(
                name: "ChunkAnalyses");
        }
    }
}
