using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Storyboarder.Domain.Model;
using Storyboarder.Infrastructure.Persistence;
using Xunit;

namespace Storyboarder.Tests.Persistence;

// Guards the update path: applying the newest EF migration over a database populated by the previous
// release must never wipe an in-flight conversion. If a future migration is destructive, this fails.
public class MigrationSafetyTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "storyboarder-migrations", Guid.NewGuid().ToString("N"));

    public MigrationSafetyTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private DbContextOptions<StoryboarderDbContext> Options(string dbPath) =>
        new DbContextOptionsBuilder<StoryboarderDbContext>().UseSqlite($"Data Source={dbPath}").Options;

    [Fact]
    public void UpgradingToLatestMigration_PreservesExistingProgress()
    {
        var dbPath = Path.Combine(dir, "storyboarder.db");
        var options = Options(dbPath);

        var migrations = new StoryboarderDbContext(options).Database.GetMigrations().ToList();
        Assert.True(migrations.Count >= 2, "Need at least two migrations to exercise an upgrade.");
        var previousRelease = migrations[^2];

        var novelId = Guid.NewGuid();
        using (var db = new StoryboarderDbContext(options))
        {
            // Bring the schema up to the previous release, then seed a mid-flight conversion with raw
            // SQL. Seeding via the EF model would include columns a later migration adds (the one under
            // test), which don't exist at this schema yet; raw SQL matches the previous schema exactly.
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(previousRelease);
            var ticks = DateTimeOffset.UtcNow.UtcTicks;
            // LastError is nullable and omitted, so it defaults to NULL — this also sidesteps having to
            // bind a DBNull parameter, which the provider can't map.
            db.Database.ExecuteSqlRaw(
                "INSERT INTO Novels (Id, Title, GoogleDocId, GoogleDocName, ArtStyle, Stage, " +
                "DesignBibleConsolidated, StoryboardCursor, ContinuitySummary, CreatedAtUtc, UpdatedAtUtc) " +
                "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10})",
                novelId, "Month-long book", "doc", "", ArtStyles.Default,
                (int)ProjectStage.RenderingImages, 0, 0, "", ticks, ticks);
            db.Database.ExecuteSqlRaw(
                "INSERT INTO SourceTexts (Id, NovelId, FullText, CharacterCount) VALUES ({0}, {1}, {2}, {3})",
                Guid.NewGuid(), novelId, "half-converted manuscript", 25);
        }
        SqliteConnection.ClearAllPools();

        // Apply the newest migration, exactly as MainApplication does on the first launch after an update.
        using (var db = new StoryboarderDbContext(options))
            db.Database.Migrate();
        SqliteConnection.ClearAllPools();

        using (var db = new StoryboarderDbContext(options))
        {
            var novel = Assert.Single(db.Novels.ToList());
            Assert.Equal(novelId, novel.Id);
            Assert.Equal(ProjectStage.RenderingImages, novel.Stage);
            Assert.Equal("half-converted manuscript", db.SourceTexts.Single().FullText);
        }
    }
}
