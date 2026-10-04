using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Index.Infrastructure.Persistence;
using Xunit;

namespace Index.Tests.Services;

/// <summary>
/// Index installs over the old Ledger under the same package id, so a phone's <c>ledger.db</c> arrives
/// already shaped by the first migration alone — balances as <c>REAL</c>, one bank, one account — and
/// every later migration has to carry it forward without losing a row. The other migration tests build
/// from an empty database, which is the one case an upgrading phone is not in.
/// </summary>
public class OldLedgerUpgradeTests
{
    /// <summary>The only migration the old Ledger app shipped with.</summary>
    private const string OldLedgersLastMigration = "20260718000000_InitialCreate";

    [Fact]
    public void ADatabaseTheOldLedgerBuiltUpgradesWithItsBanksAndBalancesIntact()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<LedgerDbContext>().UseSqlite(connection).Options;

        var institutionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        using (var old = new LedgerDbContext(options))
        {
            old.GetService<IMigrator>().Migrate(OldLedgersLastMigration);
            Insert(connection, institutionId, accountId);
        }

        using var upgraded = new LedgerDbContext(options);
        upgraded.Database.Migrate();

        Assert.Empty(upgraded.Database.GetPendingMigrations());
        var bank = upgraded.Institutions.Include(institution => institution.Accounts).Single();
        Assert.Equal(institutionId, bank.Id);
        Assert.Equal("access-example-token", bank.AccessToken);
        Assert.Equal("Example Bank", bank.Name);
        var account = Assert.Single(bank.Accounts);
        Assert.Equal(accountId, account.Id);
        Assert.Equal(1234.5m, account.CurrentBalance);
        Assert.Equal(25m, account.NextPaymentAmount);
    }

    private static void Insert(SqliteConnection connection, Guid institutionId, Guid accountId)
    {
        using var bank = connection.CreateCommand();
        bank.CommandText =
            "INSERT INTO Institutions (Id, ItemId, AccessToken, Name, RefreshStatus, Attempts, CreatedAtUtc) "
            + "VALUES ($id, 'item-example', 'access-example-token', 'Example Bank', 0, 0, 0)";
        bank.Parameters.AddWithValue("$id", institutionId.ToString().ToUpperInvariant());
        bank.ExecuteNonQuery();

        using var account = connection.CreateCommand();
        account.CommandText =
            "INSERT INTO Accounts (Id, InstitutionId, AccountId, Name, Kind, CurrentBalance, NextPaymentAmount) "
            + "VALUES ($id, $institution, 'acct-example', 'Example Checking', 0, 1234.5, 25.0)";
        account.Parameters.AddWithValue("$id", accountId.ToString().ToUpperInvariant());
        account.Parameters.AddWithValue("$institution", institutionId.ToString().ToUpperInvariant());
        account.ExecuteNonQuery();
    }
}
