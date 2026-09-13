using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Folio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Novels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    GoogleDocId = table.Column<string>(type: "TEXT", nullable: false),
                    GoogleDocName = table.Column<string>(type: "TEXT", nullable: false),
                    ArtStyle = table.Column<string>(type: "TEXT", nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DraftNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviousDraftId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DesignBibleConsolidated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChaptersDetected = table.Column<bool>(type: "INTEGER", nullable: false),
                    VoiceAnalyzed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContinuitySummary = table.Column<string>(type: "TEXT", nullable: false),
                    StageAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    RateLimitedUntilUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    ArchiveFolderId = table.Column<string>(type: "TEXT", nullable: true),
                    ArchivedAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Novels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BookAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsolidatedJson = table.Column<string>(type: "TEXT", nullable: true),
                    CraftJson = table.Column<string>(type: "TEXT", nullable: true),
                    VoiceJson = table.Column<string>(type: "TEXT", nullable: true),
                    PdfStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    PdfPath = table.Column<string>(type: "TEXT", nullable: true),
                    ArchivedDriveFileId = table.Column<string>(type: "TEXT", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "Chunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    StartOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    EndOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    ChapterNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ChapterLabel = table.Column<string>(type: "TEXT", nullable: false),
                    AnalysisStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    PartialBibleJson = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chunks_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DesignRefs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CanonicalDescription = table.Column<string>(type: "TEXT", nullable: false),
                    Scale = table.Column<string>(type: "TEXT", nullable: false),
                    AliasesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ChunkMentions = table.Column<int>(type: "INTEGER", nullable: false),
                    NeedsSheet = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReferenceSheetStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesignRefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DesignRefs_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Scenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    ChapterNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ChapterLabel = table.Column<string>(type: "TEXT", nullable: false),
                    StartOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    EndOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    ImagePrompt = table.Column<string>(type: "TEXT", nullable: true),
                    ImageStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scenes_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceTexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FullText = table.Column<string>(type: "TEXT", nullable: false),
                    CharacterCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceTexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceTexts_Novels_NovelId",
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
                name: "IX_Chapters_NovelId_Number",
                table: "Chapters",
                columns: new[] { "NovelId", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_ChunkAnalyses_NovelId_Ordinal",
                table: "ChunkAnalyses",
                columns: new[] { "NovelId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_ChunkAnalyses_Status_NextAttemptAtUtc",
                table: "ChunkAnalyses",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Chunks_AnalysisStatus_NextAttemptAtUtc",
                table: "Chunks",
                columns: new[] { "AnalysisStatus", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Chunks_NovelId_Ordinal",
                table: "Chunks",
                columns: new[] { "NovelId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_DesignRefs_NovelId",
                table: "DesignRefs",
                column: "NovelId");

            migrationBuilder.CreateIndex(
                name: "IX_DesignRefs_ReferenceSheetStatus_NextAttemptAtUtc",
                table: "DesignRefs",
                columns: new[] { "ReferenceSheetStatus", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Novels_ProjectId_DraftNumber",
                table: "Novels",
                columns: new[] { "ProjectId", "DraftNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Novels_Stage",
                table: "Novels",
                column: "Stage");

            migrationBuilder.CreateIndex(
                name: "IX_Scenes_ImageStatus_NextAttemptAtUtc",
                table: "Scenes",
                columns: new[] { "ImageStatus", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Scenes_NovelId_Number",
                table: "Scenes",
                columns: new[] { "NovelId", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceTexts_NovelId",
                table: "SourceTexts",
                column: "NovelId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookAnalyses");

            migrationBuilder.DropTable(
                name: "Chapters");

            migrationBuilder.DropTable(
                name: "ChunkAnalyses");

            migrationBuilder.DropTable(
                name: "Chunks");

            migrationBuilder.DropTable(
                name: "DesignRefs");

            migrationBuilder.DropTable(
                name: "Scenes");

            migrationBuilder.DropTable(
                name: "SourceTexts");

            migrationBuilder.DropTable(
                name: "Novels");
        }
    }
}
