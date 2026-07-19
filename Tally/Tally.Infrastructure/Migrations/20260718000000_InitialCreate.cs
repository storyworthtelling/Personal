using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tally.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Institutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    AccessToken = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    RefreshStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastRefreshedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Institutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstitutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    OfficialName = table.Column<string>(type: "TEXT", nullable: true),
                    Mask = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Subtype = table.Column<string>(type: "TEXT", nullable: true),
                    IsoCurrencyCode = table.Column<string>(type: "TEXT", nullable: true),
                    CurrentBalance = table.Column<double>(type: "REAL", nullable: true),
                    AvailableBalance = table.Column<double>(type: "REAL", nullable: true),
                    CreditLimit = table.Column<double>(type: "REAL", nullable: true),
                    BalanceAsOfUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastStatementBalance = table.Column<double>(type: "REAL", nullable: true),
                    NextPaymentDueDate = table.Column<long>(type: "INTEGER", nullable: true),
                    NextPaymentAmount = table.Column<double>(type: "REAL", nullable: true),
                    Apr = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_InstitutionId_AccountId",
                table: "Accounts",
                columns: new[] { "InstitutionId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_RefreshStatus_NextAttemptAtUtc",
                table: "Institutions",
                columns: new[] { "RefreshStatus", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Accounts");
            migrationBuilder.DropTable(name: "Institutions");
        }
    }
}
