using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookAnalysisVoiceJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Null on existing reports; the voice-consistency step runs on the next cycle for any book
            // whose report PDF is still outstanding, and a finished book is simply left as it was.
            migrationBuilder.AddColumn<string>(
                name: "VoiceJson",
                table: "BookAnalyses",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "VoiceJson", table: "BookAnalyses");
        }
    }
}
