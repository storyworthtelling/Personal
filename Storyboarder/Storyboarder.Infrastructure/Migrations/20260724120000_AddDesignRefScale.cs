using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDesignRefScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Scale",
                table: "DesignRefs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Scale",
                table: "DesignRefs");
        }
    }
}
