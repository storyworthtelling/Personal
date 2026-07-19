using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tally.Infrastructure.Persistence;

namespace Tally.Tests.Support;

// A throwaway SQLite database held in memory for the lifetime of one test. The connection stays open
// so the schema persists across the DbContext instances a test creates. Implements IDbContextFactory
// so it can be handed straight to the BalanceRefreshProcessor and AccountService.
public sealed class TestDatabase : IDbContextFactory<TallyDbContext>, IDisposable
{
    private readonly SqliteConnection connection;
    private readonly DbContextOptions<TallyDbContext> options;

    public TestDatabase()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        options = new DbContextOptionsBuilder<TallyDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    public TallyDbContext CreateDbContext() => new(options);

    public void Dispose() => connection.Dispose();
}
