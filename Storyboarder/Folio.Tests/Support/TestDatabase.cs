using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Folio.Infrastructure.Persistence;

namespace Folio.Tests.Support;

// A throwaway SQLite database held in memory for the lifetime of one test. The connection stays
// open so the schema persists across the DbContext instances a test creates. Implements
// IDbContextFactory so it can be handed straight to the ReviewProcessor.
public sealed class TestDatabase : IDbContextFactory<FolioDbContext>, IDisposable
{
    private readonly SqliteConnection connection;
    private readonly DbContextOptions<FolioDbContext> options;

    public TestDatabase()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        options = new DbContextOptionsBuilder<FolioDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public FolioDbContext NewContext() => new(options);

    public FolioDbContext CreateDbContext() => new(options);

    public void Dispose() => connection.Dispose();
}
