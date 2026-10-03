using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Index.Infrastructure.Persistence;
using Xunit;

namespace Index.Tests.Services;

/// <summary>
/// The tests build their schema with <c>EnsureCreated</c>, from the model — so a column added to an entity
/// works in every test whether or not anyone wrote the migration the phone needs. The phone runs the
/// MIGRATIONS. This asks EF the one question that closes the gap: does the model snapshot the migrations
/// leave behind match the model in code? A property added without <c>dotnet ef migrations add</c> (Phase 3.3's
/// <c>Account.BalanceSource</c>, for one) fails here instead of on a device.
/// </summary>
public class LedgerMigrationDriftTests
{
    [Fact]
    public void TheMigrationsLeaveTheSchemaTheModelDescribes()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<LedgerDbContext>().UseSqlite(connection).Options;
        using var context = new LedgerDbContext(options);

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The model has changes no migration carries. Run: LedgerTfms=net10.0 dotnet ef migrations add <Name> "
            + "--project Ledger/Ledger.Infrastructure --framework net10.0");
    }

    [Fact]
    public void EveryMigrationAppliesToAnEmptyDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<LedgerDbContext>().UseSqlite(connection).Options;
        using var context = new LedgerDbContext(options);

        context.Database.Migrate();

        Assert.Empty(context.Database.GetPendingMigrations());
    }
}
