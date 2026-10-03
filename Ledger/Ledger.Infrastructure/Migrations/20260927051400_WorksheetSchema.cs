using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Index.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorksheetSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "IncomeOverride",
                table: "BudgetMonths",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorksheetCategoryOrderJson",
                table: "BudgetMonths",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncomeOverride",
                table: "BudgetMonths");

            migrationBuilder.DropColumn(
                name: "WorksheetCategoryOrderJson",
                table: "BudgetMonths");
        }
    }
}
