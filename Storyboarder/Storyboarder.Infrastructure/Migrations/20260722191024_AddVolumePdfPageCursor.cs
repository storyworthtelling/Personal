using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVolumePdfPageCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PdfPageCursor",
                table: "Volumes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PdfPageCursor",
                table: "Volumes");
        }
    }
}
