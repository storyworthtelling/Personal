using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookAnalysisCraftJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The craft half of the revision report is consolidated by its own Claude call, so it is
            // stored beside the story half rather than inside it: the two then succeed, fail, and
            // retry independently, and the (billed) call that already worked is never repeated
            // because the other one failed. Purely additive — an in-flight conversion is untouched
            // and simply has no craft sections until its analysis stage runs.
            migrationBuilder.AddColumn<string>(
                name: "CraftJson",
                table: "BookAnalyses",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CraftJson",
                table: "BookAnalyses");
        }
    }
}
