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

            // Rows written in the schema of that day, not through today's model, which has columns that
            // table does not have yet.
            foreach (var stage in new[] { ProjectStage.ComposingAnalysis, ProjectStage.Completed, ProjectStage.Failed })
            {
                var id = Guid.NewGuid();
                InsertOldRow(db, "Novels", new Dictionary<string, object?>
                {
                    ["Id"] = id.ToString().ToUpperInvariant(), ["ProjectId"] = id.ToString().ToUpperInvariant(),
                    ["Title"] = stage.ToString(), ["GoogleDocId"] = "doc", ["Stage"] = (int)stage,
                    ["VoicedLine"] = "Draft 1 is complete.", ["VoicedLineStage"] = (int)stage,
                });
            }
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

    // Inserts one row into a table as an older migration left it: the values given, and for every other
    // column that may not be null and has no default, a zero or an empty string.
    private static void InsertOldRow(FolioDbContext db, string table, IReadOnlyDictionary<string, object?> values)
    {
        var connection = db.Database.GetDbConnection();
        connection.Open();
        var columns = new Dictionary<string, object?>(values);
        using (var info = connection.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                var type = reader.GetString(2);
                var notNull = reader.GetInt32(3) == 1;
                var hasDefault = !reader.IsDBNull(4);
                if (notNull && !hasDefault && !columns.ContainsKey(name))
                    columns[name] = type.Equals("TEXT", StringComparison.OrdinalIgnoreCase) ? string.Empty : 0;
            }
        }

        using var insert = connection.CreateCommand();
        var names = columns.Keys.ToList();
        insert.CommandText = $"INSERT INTO \"{table}\" ({string.Join(", ", names.Select(n => $"\"{n}\""))}) "
            + $"VALUES ({string.Join(", ", names.Select((_, i) => $"$p{i}"))})";
        for (var i = 0; i < names.Count; i++)
        {
            var parameter = insert.CreateParameter();
            parameter.ParameterName = $"$p{i}";
            parameter.Value = columns[names[i]] ?? DBNull.Value;
            insert.Parameters.Add(parameter);
        }
        insert.ExecuteNonQuery();
    }
}
