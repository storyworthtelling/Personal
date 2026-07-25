using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelShotType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0 == ShotType.Standard, so every existing panel keeps laying out on the uniform grid it
            // was composed with — the weighted layout only diverges for panels tagged Wide/Splash.
            migrationBuilder.AddColumn<int>(
                name: "ShotType",
                table: "Panels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShotType",
                table: "Panels");
        }
    }
}
