using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Index.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TransactionPlaidCategoryAndPending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Pending",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PlaidCategoryDetailed",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaidCategoryPrimary",
                table: "Transactions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Pending",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PlaidCategoryDetailed",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PlaidCategoryPrimary",
                table: "Transactions");
        }
    }
}
