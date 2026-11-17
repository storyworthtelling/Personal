using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Folio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeriesBibleRestore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DriveDataId",
                table: "SeriesBibles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SeriesBibleRestoreChecked",
                table: "BookAnalyses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriveDataId",
                table: "SeriesBibles");

            migrationBuilder.DropColumn(
                name: "SeriesBibleRestoreChecked",
                table: "BookAnalyses");
        }
    }
}
