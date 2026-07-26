using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelLettering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing panels were lettered as spoken with no SFX, so 0 (Speech) and NULL are exactly the
            // old behaviour — they re-render identically.
            migrationBuilder.AddColumn<int>(
                name: "DialogueKind",
                table: "Panels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Sfx",
                table: "Panels",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DialogueKind", table: "Panels");
            migrationBuilder.DropColumn(name: "Sfx", table: "Panels");
        }
    }
}
