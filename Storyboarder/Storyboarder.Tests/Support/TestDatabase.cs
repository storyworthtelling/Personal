using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Storyboarder.Infrastructure.Persistence;

namespace Storyboarder.Tests.Support;

// A throwaway SQLite database held in memory for the lifetime of one test. The connection stays
// open so the schema persists across the DbContext instances a test creates.
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly DbContextOptions<StoryboarderDbContext> options;

    public TestDatabase()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        options = new DbContextOptionsBuilder<StoryboarderDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public StoryboarderDbContext NewContext() => new(options);

    public void Dispose() => connection.Dispose();
}
