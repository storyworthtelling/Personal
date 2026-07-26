using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNovelVoiceAnalyzed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing drafts were ingested before voice analysis existed; leaving them false means the
            // owner's voice profile is refined the next time their manuscript is (re)ingested, never
            // retroactively re-billed for books already converted.
            migrationBuilder.AddColumn<bool>(
                name: "VoiceAnalyzed",
                table: "Novels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "VoiceAnalyzed", table: "Novels");
        }
    }
}
