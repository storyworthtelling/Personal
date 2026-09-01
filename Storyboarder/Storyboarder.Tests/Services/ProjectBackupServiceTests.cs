using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.Model;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.Persistence;
using Storyboarder.Infrastructure.Services;
using Xunit;

namespace Storyboarder.Tests.Services;

public class ProjectBackupServiceTests : IDisposable
{
    private readonly string workRoot = Path.Combine(Path.GetTempPath(), "storyboarder-backup", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(workRoot))
            Directory.Delete(workRoot, recursive: true);
    }

    private (StorageOptions options, Guid novelId) SeedProject(string subdir)
    {
        var dir = Path.Combine(workRoot, subdir);
        var dbPath = Path.Combine(dir, "storyboarder.db");
        var outputRoot = Path.Combine(dir, "output");
        Directory.CreateDirectory(dir);

        var options = new DbContextOptionsBuilder<StoryboarderDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        Guid novelId;
        using (var db = new StoryboarderDbContext(options))
        {
            db.Database.EnsureCreated();
            var novel = new Novel { Title = "Long Conversion", GoogleDocId = "doc", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            db.Novels.Add(novel);
            db.SaveChanges();
            novelId = novel.Id;
        }
        SqliteConnection.ClearAllPools();

        // A rendered artifact on disk, the kind a restore must bring back.
        var panelPath = Path.Combine(outputRoot, novelId.ToString(), "panels", "p1.png");
        Directory.CreateDirectory(Path.GetDirectoryName(panelPath)!);
        File.WriteAllBytes(panelPath, [1, 2, 3, 4, 5]);

        return (new StorageOptions { DatabasePath = dbPath, OutputRoot = outputRoot }, novelId);
    }

    private static ProjectBackupService Service(StorageOptions options) => new(Options.Create(options));

    [Fact]
    public async Task ExportThenImport_RoundTripsDatabaseRowsAndFiles()
    {
        var (source, novelId) = SeedProject("source");

        using var backup = new MemoryStream();
        await Service(source).ExportAsync(backup, CancellationToken.None);
        Assert.True(backup.Length > 0);

        // Restore into an empty target directory, as a fresh install would.
        var targetDir = Path.Combine(workRoot, "target");
        var target = new StorageOptions
        {
            DatabasePath = Path.Combine(targetDir, "storyboarder.db"),
            OutputRoot = Path.Combine(targetDir, "output")
        };
        backup.Position = 0;
        await Service(target).ImportAsync(backup, CancellationToken.None);

        var options = new DbContextOptionsBuilder<StoryboarderDbContext>().UseSqlite($"Data Source={target.DatabasePath}").Options;
        using var db = new StoryboarderDbContext(options);
        var novel = Assert.Single(db.Novels.ToList());
        Assert.Equal(novelId, novel.Id);
        Assert.Equal("Long Conversion", novel.Title);

        var restoredPanel = Path.Combine(target.OutputRoot, novelId.ToString(), "panels", "p1.png");
        Assert.True(File.Exists(restoredPanel));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(restoredPanel));
    }

    [Fact]
    public async Task Import_ReplacesExistingProject()
    {
        var (source, sourceNovelId) = SeedProject("source");
        var (target, _) = SeedProject("target"); // a different existing project

        using var backup = new MemoryStream();
        await Service(source).ExportAsync(backup, CancellationToken.None);
        backup.Position = 0;
        await Service(target).ImportAsync(backup, CancellationToken.None);

        var options = new DbContextOptionsBuilder<StoryboarderDbContext>().UseSqlite($"Data Source={target.DatabasePath}").Options;
        using var db = new StoryboarderDbContext(options);
        var novel = Assert.Single(db.Novels.ToList());
        Assert.Equal(sourceNovelId, novel.Id);
    }

    [Fact]
    public async Task Import_RejectsArchiveWithoutDatabase()
    {
        var (target, _) = SeedProject("target");

        using var notABackup = new MemoryStream();
        using (var archive = new ZipArchive(notABackup, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("readme.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("not a backup");
        }
        notABackup.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(
            () => Service(target).ImportAsync(notABackup, CancellationToken.None));
    }

    [Fact]
    public async Task Import_RejectsGarbageStream()
    {
        var (target, _) = SeedProject("target");
        using var garbage = new MemoryStream([0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11, 0x22]);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => Service(target).ImportAsync(garbage, CancellationToken.None));
    }
}
