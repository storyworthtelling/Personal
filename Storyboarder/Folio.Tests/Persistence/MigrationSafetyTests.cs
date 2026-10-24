using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Folio.Domain.Model;
using Folio.Infrastructure.Persistence;
using Xunit;

namespace Folio.Tests.Persistence;

// Guards the schema: the migrations must build the whole current model, and the model must not have
// drifted from the migration snapshot. (The Canonizer pivot is a clean-slate schema — no old graphic-novel
// data is carried across the upgrade — so this no longer asserts cross-release data preservation.)
public class MigrationSafetyTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "canonizer-migrations", Guid.NewGuid().ToString("N"));

    public MigrationSafetyTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private DbContextOptions<FolioDbContext> Options(string dbPath) =>
        new DbContextOptionsBuilder<FolioDbContext>().UseSqlite($"Data Source={dbPath}").Options;

    [Fact]
    public void Migrations_BuildTheSchema_AndModelHasNoPendingChanges()
    {
        var dbPath = Path.Combine(dir, "canonizer.db");
        var options = Options(dbPath);

        using (var db = new FolioDbContext(options))
        {
            db.Database.Migrate();
            // The model matches the migration snapshot — no property or relationship has drifted.
            Assert.False(db.Database.HasPendingModelChanges(), "The model has changes not captured in a migration.");
        }
        SqliteConnection.ClearAllPools();

        // The migrated schema round-trips a manuscript with a scene and a report.
        var manuscriptId = Guid.NewGuid();
        using (var db = new FolioDbContext(options))
        {
            var now = DateTimeOffset.UtcNow;
            var novel = new Novel
            {
                Id = manuscriptId, Title = "Quest", GoogleDocId = "doc", ArtStyle = ArtStyles.Default,
                Stage = ProjectStage.RenderingSceneImages, ProjectId = manuscriptId,
                CreatedAtUtc = now, UpdatedAtUtc = now
            };
            novel.Source = new SourceText { NovelId = manuscriptId, FullText = "text", CharacterCount = 4 };
            novel.Scenes.Add(new Scene { NovelId = manuscriptId, Number = 1, ImageStatus = WorkStatus.Pending });
            novel.Analysis = new BookAnalysis { NovelId = manuscriptId, PdfStatus = WorkStatus.Pending };
            db.Novels.Add(novel);
            db.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        using (var db = new FolioDbContext(options))
        {
            var novel = Assert.Single(db.Novels.ToList());
            Assert.Equal(manuscriptId, novel.Id);
            Assert.Equal(ProjectStage.RenderingSceneImages, novel.Stage);
            Assert.Single(db.Scenes.ToList());
            Assert.Equal("text", db.SourceTexts.Single().FullText);
        }
    }

    // A desk line stored before the model was told whether the book had finished could say "Draft 1 is
    // complete" about a book still mid-read, and it is kept until the stage moves — so it was cleared
    // once, for every book that is not Completed. A finished book's line was composed knowing it was done.
    [Fact]
    public void TheClearUnreliableVoicedLinesMigration_ClearsUnfinishedBooksOnly()
    {
        var options = Options(Path.Combine(dir, "voiced.db"));
        using (var db = new FolioDbContext(options))
        {
            db.GetService<IMigrator>().Migrate("20261022192414_RetryNotBefore");

            var now = DateTimeOffset.UtcNow;
            foreach (var stage in new[] { ProjectStage.ComposingAnalysis, ProjectStage.Completed, ProjectStage.Failed })
            {
                var id = Guid.NewGuid();
                db.Novels.Add(new Novel
                {
                    Id = id, ProjectId = id, Title = stage.ToString(), GoogleDocId = "doc", Stage = stage,
                    VoicedLine = "Draft 1 is complete.", VoicedLineStage = stage,
                    CreatedAtUtc = now, UpdatedAtUtc = now
                });
            }
            db.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        using (var db = new FolioDbContext(options))
            db.Database.Migrate();
        SqliteConnection.ClearAllPools();

        using (var db = new FolioDbContext(options))
        {
            var novels = db.Novels.ToDictionary(n => n.Title);
            Assert.Equal(string.Empty, novels["ComposingAnalysis"].VoicedLine);
            Assert.Null(novels["ComposingAnalysis"].VoicedLineStage);
            Assert.Equal(string.Empty, novels["Failed"].VoicedLine);
            Assert.Equal("Draft 1 is complete.", novels["Completed"].VoicedLine);
            Assert.Equal(ProjectStage.Completed, novels["Completed"].VoicedLineStage);
        }
    }
}
