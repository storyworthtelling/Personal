using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Folio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeriesBibleDriveDoc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BackedUpAtUtc",
                table: "SeriesBibles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriveDocId",
                table: "SeriesBibles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackedUpAtUtc",
                table: "SeriesBibles");

            migrationBuilder.DropColumn(
                name: "DriveDocId",
                table: "SeriesBibles");
        }
    }
}
