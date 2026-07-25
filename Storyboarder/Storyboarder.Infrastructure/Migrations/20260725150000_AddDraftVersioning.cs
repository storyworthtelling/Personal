using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "Novels",
                type: "TEXT",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<int>(
                name: "DraftNumber",
                table: "Novels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousDraftId",
                table: "Novels",
                type: "TEXT",
                nullable: true);

            // Every existing novel is its own one-draft project. Backfilling ProjectId = Id (rather than
            // leaving it Guid.Empty) is load-bearing: otherwise all pre-existing books would collapse into
            // a single project keyed on the empty guid, and adding a draft to one would list every other
            // book as a sibling draft.
            migrationBuilder.Sql("UPDATE Novels SET ProjectId = Id;");

            migrationBuilder.CreateIndex(
                name: "IX_Novels_ProjectId_DraftNumber",
                table: "Novels",
                columns: new[] { "ProjectId", "DraftNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Novels_ProjectId_DraftNumber",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "DraftNumber",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "PreviousDraftId",
                table: "Novels");
        }
    }
}
