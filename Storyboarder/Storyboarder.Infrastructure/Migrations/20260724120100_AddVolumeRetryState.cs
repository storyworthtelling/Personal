using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVolumeRetryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "Volumes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "Volumes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NextAttemptAtUtc",
                table: "Volumes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Volumes_PdfStatus_NextAttemptAtUtc",
                table: "Volumes",
                columns: new[] { "PdfStatus", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Volumes_PdfStatus_NextAttemptAtUtc",
                table: "Volumes");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "Volumes");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "Volumes");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                table: "Volumes");
        }
    }
}
