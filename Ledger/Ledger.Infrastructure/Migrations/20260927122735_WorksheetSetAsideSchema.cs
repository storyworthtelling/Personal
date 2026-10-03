using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Index.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorksheetSetAsideSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PrintedExtraPaymentLastMonthWholeDollars",
                table: "BudgetMonths",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PrintedZeroBasedTargetWholeDollars",
                table: "BudgetMonths",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorksheetSetAsideOrderJson",
                table: "BudgetMonths",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorksheetSetAsideAmounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BudgetMonthId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SavingsAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LimitAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    LimitUnreadable = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorksheetSetAsideAmounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetSetAsideAmounts_BudgetMonthId_SavingsAccountId",
                table: "WorksheetSetAsideAmounts",
                columns: new[] { "BudgetMonthId", "SavingsAccountId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorksheetSetAsideAmounts");

            migrationBuilder.DropColumn(
                name: "PrintedExtraPaymentLastMonthWholeDollars",
                table: "BudgetMonths");

            migrationBuilder.DropColumn(
                name: "PrintedZeroBasedTargetWholeDollars",
                table: "BudgetMonths");

            migrationBuilder.DropColumn(
                name: "WorksheetSetAsideOrderJson",
                table: "BudgetMonths");
        }
    }
}
