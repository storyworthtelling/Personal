using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelSceneBatchState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Per-scene batch rendering: Claude tags scene boundaries (NewScene) and each panel now tracks the
            // scene job it's in (BatchJobId) and when it was submitted (BatchSubmittedAtUtc), so a novel runs
            // several small jobs at once and a wedged job can be given up on by age. Existing panels default to
            // NewScene = 0 (no boundary) with no job in flight — identical to the old single-job behaviour.
            migrationBuilder.AddColumn<bool>(
                name: "NewScene",
                table: "Panels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BatchJobId",
                table: "Panels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BatchSubmittedAtUtc",
                table: "Panels",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "NewScene", table: "Panels");
            migrationBuilder.DropColumn(name: "BatchJobId", table: "Panels");
            migrationBuilder.DropColumn(name: "BatchSubmittedAtUtc", table: "Panels");
        }
    }
}
