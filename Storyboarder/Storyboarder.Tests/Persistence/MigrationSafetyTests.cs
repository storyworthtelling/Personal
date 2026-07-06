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

        Guid novelId;
        using (var db = new StoryboarderDbContext(options))
        {
            // Bring the schema up to the previous release, then seed a mid-flight conversion.
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(previousRelease);
            var novel = new Novel
            {
                Title = "Month-long book",
                GoogleDocId = "doc",
                Stage = ProjectStage.RenderingImages,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            novel.Source = new SourceText { NovelId = novel.Id, FullText = "half-converted manuscript", CharacterCount = 25 };
            db.Novels.Add(novel);
            db.SaveChanges();
            novelId = novel.Id;
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
