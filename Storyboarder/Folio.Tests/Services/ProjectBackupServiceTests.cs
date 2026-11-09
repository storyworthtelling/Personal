using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Folio.Domain.Model;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Persistence;
using Folio.Infrastructure.Services;
using Xunit;

namespace Folio.Tests.Services;

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
        var dbPath = Path.Combine(dir, "folio.db");
        var outputRoot = Path.Combine(dir, "output");
        Directory.CreateDirectory(dir);

        var options = new DbContextOptionsBuilder<FolioDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        Guid novelId;
        using (var db = new FolioDbContext(options))
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

    // A backup taken before the rename carries the old entry name. It is the one copy of books the
    // device may no longer have, so a restore must still read it.
    [Fact]
    public async Task Import_ReadsABackupWrittenUnderTheOldDatabaseEntryName()
    {
        var (source, novelId) = SeedProject("legacy-source");

        using var legacy = new MemoryStream();
        using (var archive = new ZipArchive(legacy, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("storyboarder.db");
            using var entryStream = entry.Open();
            using var db = File.OpenRead(source.DatabasePath);
            await db.CopyToAsync(entryStream);
        }

        var targetDir = Path.Combine(workRoot, "legacy-target");
        var target = new StorageOptions
        {
            DatabasePath = Path.Combine(targetDir, "folio.db"),
            OutputRoot = Path.Combine(targetDir, "output")
        };

        legacy.Position = 0;
        await Service(target).ImportAsync(legacy, CancellationToken.None);

        var options = new DbContextOptionsBuilder<FolioDbContext>().UseSqlite($"Data Source={target.DatabasePath}").Options;
        using var restored = new FolioDbContext(options);
        Assert.Equal(novelId, Assert.Single(restored.Novels.ToList()).Id);
    }

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
            DatabasePath = Path.Combine(targetDir, "folio.db"),
            OutputRoot = Path.Combine(targetDir, "output")
        };
        backup.Position = 0;
        await Service(target).ImportAsync(backup, CancellationToken.None);

        var options = new DbContextOptionsBuilder<FolioDbContext>().UseSqlite($"Data Source={target.DatabasePath}").Options;
        using var db = new FolioDbContext(options);
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

        var options = new DbContextOptionsBuilder<FolioDbContext>().UseSqlite($"Data Source={target.DatabasePath}").Options;
        using var db = new FolioDbContext(options);
        var novel = Assert.Single(db.Novels.ToList());
        Assert.Equal(sourceNovelId, novel.Id);
    }

    // Error-handling §16: the restore overwrote the database and then deleted the output folder before copying the
    // new one in, so low storage part way through left a database pointing at pictures and PDFs that were gone.
    // Now nothing live is touched until the new output is fully in place beside the old.

    [Fact]
    public async Task Import_WithoutRoomForTheNewProject_ChangesNothing()
    {
        var (source, _) = SeedProject("source");
        var (target, targetNovelId) = SeedProject("target");
        using var backup = new MemoryStream();
        await Service(source).ExportAsync(backup, CancellationToken.None);
        backup.Position = 0;

        var tight = new ProjectBackupService(Options.Create(target), freeBytes: _ => 10);
        var thrown = await Assert.ThrowsAsync<IOException>(() => tight.ImportAsync(backup, CancellationToken.None));

        Assert.Contains("space", thrown.Message, StringComparison.OrdinalIgnoreCase);
        AssertStillTheTarget(target, targetNovelId);
    }

    [Fact]
    public async Task Import_ThatCannotCopyTheOutput_LeavesTheLiveProjectAsItWas()
    {
        var (source, _) = SeedProject("source");
        var (target, targetNovelId) = SeedProject("target");
        using var backup = new MemoryStream();
        await Service(source).ExportAsync(backup, CancellationToken.None);
        backup.Position = 0;

        // Something already sits where the new output would be copied, so the copy fails before the swap.
        File.WriteAllText(target.OutputRoot.TrimEnd(Path.DirectorySeparatorChar) + ".restoring", "in the way");

        await Assert.ThrowsAnyAsync<IOException>(() => Service(target).ImportAsync(backup, CancellationToken.None));

        AssertStillTheTarget(target, targetNovelId);
    }

    [Fact]
    public async Task Import_LeavesNoStagingOrSnapshotBehind()
    {
        var (source, _) = SeedProject("source");
        var (target, _) = SeedProject("target");
        using var backup = new MemoryStream();
        await Service(source).ExportAsync(backup, CancellationToken.None);
        backup.Position = 0;

        await Service(target).ImportAsync(backup, CancellationToken.None);

        var siblings = Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(target.DatabasePath)!).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["folio.db", "output"], siblings);
    }

    private static void AssertStillTheTarget(StorageOptions target, Guid targetNovelId)
    {
        SqliteConnection.ClearAllPools();
        var options = new DbContextOptionsBuilder<FolioDbContext>().UseSqlite($"Data Source={target.DatabasePath}").Options;
        using (var db = new FolioDbContext(options))
        {
            Assert.Equal(targetNovelId, Assert.Single(db.Novels.ToList()).Id);
        }
        SqliteConnection.ClearAllPools();
        Assert.True(File.Exists(Path.Combine(target.OutputRoot, targetNovelId.ToString(), "panels", "p1.png")));
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
