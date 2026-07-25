using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelSourceOffsets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Absolute span of the source text each panel was storyboarded from. Existing rows land at
            // 0..0 (a zero-width span); that is harmless — the incremental re-run treats a zero-width
            // panel as "source unknown", so a legacy draft simply re-renders rather than mis-reusing.
            migrationBuilder.AddColumn<int>(
                name: "SourceStartOffset",
                table: "Panels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceEndOffset",
                table: "Panels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceStartOffset",
                table: "Panels");

            migrationBuilder.DropColumn(
                name: "SourceEndOffset",
                table: "Panels");
        }
    }
}
