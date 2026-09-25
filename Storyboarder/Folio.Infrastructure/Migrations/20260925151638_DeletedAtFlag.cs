using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Folio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DeletedAtFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DeletedAtUtc",
                table: "Novels",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Novels");
        }
    }
}
