using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Index.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SnowballSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SnowballPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoanAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FocusOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ExtraPaymentAmount = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnowballPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SnowballSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WeekOf = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Balance = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnowballSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SnowballPlans_LoanAccountId",
                table: "SnowballPlans",
                column: "LoanAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SnowballSnapshots_AccountId_WeekOf",
                table: "SnowballSnapshots",
                columns: new[] { "AccountId", "WeekOf" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SnowballPlans");

            migrationBuilder.DropTable(
                name: "SnowballSnapshots");
        }
    }
}
