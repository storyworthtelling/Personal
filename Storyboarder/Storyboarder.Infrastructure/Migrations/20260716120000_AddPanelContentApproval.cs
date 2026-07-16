using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelContentApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlockedCategoriesJson",
                table: "Panels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ApprovedForRelaxedRender",
                table: "Panels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PromptRewrittenForSafety",
                table: "Panels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Skipped",
                table: "Panels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockedCategoriesJson",
                table: "Panels");

            migrationBuilder.DropColumn(
                name: "ApprovedForRelaxedRender",
                table: "Panels");

            migrationBuilder.DropColumn(
                name: "PromptRewrittenForSafety",
                table: "Panels");

            migrationBuilder.DropColumn(
                name: "Skipped",
                table: "Panels");
        }
    }
}
