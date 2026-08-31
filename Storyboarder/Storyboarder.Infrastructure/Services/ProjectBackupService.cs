using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Storyboarder.Infrastructure.Configuration;

namespace Storyboarder.Infrastructure.Services;

// Exports the whole conversion — the SQLite database plus every rendered image and PDF — into a
// single portable zip, and restores one back. This is the recovery net for a long conversion: a
// reinstall, a wiped device, or a new phone can pick the backup and continue instead of restarting.
// Pure .NET (System.IO.Compression + Microsoft.Data.Sqlite); the Android head only supplies the
// stream from the system file picker.
public sealed class ProjectBackupService
{
    private const string DatabaseEntryName = "storyboarder.db";
    private const string OutputPrefix = "output/";

    // A SQLite database file always begins with this 16-byte magic header.
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    private readonly string databasePath;
    private readonly string outputRoot;

    public ProjectBackupService(IOptions<StorageOptions> options)
    {
        databasePath = Path.GetFullPath(options.Value.DatabasePath);
        outputRoot = Path.GetFullPath(options.Value.OutputRoot);
    }

    // Writes a backup zip to the destination stream. The database is snapshotted through SQLite's
    // online-backup API so the copy is consistent even while the worker is mid-write.
    public async Task ExportAsync(Stream destination, CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException($"No Storyboarder database found at '{databasePath}' to back up.", databasePath);

        var snapshotPath = Path.Combine(Path.GetTempPath(), $"storyboarder-backup-{Guid.NewGuid():N}.db");
        try
        {
            SnapshotDatabase(snapshotPath);

            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            await AddFileEntry(archive, snapshotPath, DatabaseEntryName, cancellationToken);

            if (Directory.Exists(outputRoot))
            {
                foreach (var file in Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(outputRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                    await AddFileEntry(archive, file, OutputPrefix + relative, cancellationToken);
                }
            }
        }
        finally
        {
            if (File.Exists(snapshotPath))
                File.Delete(snapshotPath);
        }
    }

    // Restores a backup zip over the live project: extracts to a staging folder, validates it, then
    // swaps in the database and output tree. A malformed archive is rejected before anything is
    // touched, so a bad file can never corrupt an in-progress conversion.
    public async Task ImportAsync(Stream source, CancellationToken cancellationToken)
    {
        var staging = Path.Combine(Path.GetTempPath(), $"storyboarder-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            await ExtractArchive(source, staging, cancellationToken);

            var stagedDatabase = Path.Combine(staging, DatabaseEntryName);
            if (!File.Exists(stagedDatabase))
                throw new InvalidDataException("This file is not a Storyboarder backup: it contains no database.");
            ValidateSqlite(stagedDatabase);

            // Release any pooled handles the live process holds on the database before overwriting it.
            SqliteConnection.ClearAllPools();

            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            File.Copy(stagedDatabase, databasePath, overwrite: true);
            // Drop stale journal sidecars so the restored database isn't read through an old journal.
            foreach (var sidecar in new[] { databasePath + "-wal", databasePath + "-shm", databasePath + "-journal" })
                if (File.Exists(sidecar))
                    File.Delete(sidecar);

            var stagedOutput = Path.Combine(staging, "output");
            if (Directory.Exists(outputRoot))
                Directory.Delete(outputRoot, recursive: true);
            if (Directory.Exists(stagedOutput))
                CopyDirectory(stagedOutput, outputRoot);
            else
                Directory.CreateDirectory(outputRoot);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private void SnapshotDatabase(string snapshotPath)
    {
        using var source = new SqliteConnection($"Data Source={databasePath}");
        using var snapshot = new SqliteConnection($"Data Source={snapshotPath}");
        source.Open();
        snapshot.Open();
        source.BackupDatabase(snapshot);
    }

    private static async Task ExtractArchive(Stream source, string staging, CancellationToken cancellationToken)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("This file is not a readable Storyboarder backup (the archive could not be opened).", ex);
        }

        using (archive)
        {
            var stagingFull = Path.GetFullPath(staging);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/'))
                    continue; // directory marker

                var target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!target.StartsWith(stagingFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException($"Backup entry '{entry.FullName}' escapes the restore folder.");

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var entryStream = entry.Open();
                await using var fileStream = File.Create(target);
                await entryStream.CopyToAsync(fileStream, cancellationToken);
            }
        }
    }

    private static async Task AddFileEntry(ZipArchive archive, string filePath, string entryName, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var entryStream = entry.Open();
        await using var fileStream = File.OpenRead(filePath);
        await fileStream.CopyToAsync(entryStream, cancellationToken);
    }

    private static void ValidateSqlite(string path)
    {
        var header = new byte[SqliteHeader.Length];
        using (var stream = File.OpenRead(path))
        {
            if (stream.Read(header, 0, header.Length) < header.Length)
                throw new InvalidDataException("The backup's database file is truncated or empty.");
        }

        if (!header.AsSpan().SequenceEqual(SqliteHeader))
            throw new InvalidDataException("The backup's database file is not a valid SQLite database.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }
}
