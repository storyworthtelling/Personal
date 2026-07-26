using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storyboarder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchRenderState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BatchJobId",
                table: "Novels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BatchSubmittedAtUtc",
                table: "Novels",
                type: "INTEGER",
                nullable: true);

            // Existing panels were rendered synchronously, so 0 (never batched) is correct — they'll never
            // be picked up by the batch submit even if batch mode is later switched on.
            migrationBuilder.AddColumn<bool>(
                name: "BatchAttempted",
                table: "Panels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BatchJobId", table: "Novels");
            migrationBuilder.DropColumn(name: "BatchSubmittedAtUtc", table: "Novels");
            migrationBuilder.DropColumn(name: "BatchAttempted", table: "Panels");
        }
    }
}
