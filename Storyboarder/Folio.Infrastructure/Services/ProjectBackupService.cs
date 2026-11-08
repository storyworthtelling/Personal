using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Folio.Infrastructure.Configuration;

namespace Folio.Infrastructure.Services;

// Exports the whole conversion — the SQLite database plus every rendered image and PDF — into a
// single portable zip, and restores one back. This is the recovery net for a long conversion: a
// reinstall, a wiped device, or a new phone can pick the backup and continue instead of restarting.
// Pure .NET (System.IO.Compression + Microsoft.Data.Sqlite); the Android head only supplies the
// stream from the system file picker.
public sealed class ProjectBackupService
{
    private const string DatabaseEntryName = "folio.db";

    // A backup written before the rename carries the old entry name, and a restore is the one moment
    // the owner is asking for data the device no longer has. Written under the new name, read under
    // either — dropping the old one would turn every backup taken as Canonizer into "not a backup".
    private static readonly string[] DatabaseEntryNames = [DatabaseEntryName, "storyboarder.db"];
    private const string OutputPrefix = "output/";

    // A SQLite database file always begins with this 16-byte magic header.
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    private readonly string databasePath;
    private readonly string outputRoot;
    private readonly Func<string, long> freeBytes;

    public ProjectBackupService(IOptions<StorageOptions> options)
        : this(options, path => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace)
    {
    }

    /// <param name="freeBytes">The free space on the volume holding a path; a test passes a small number.</param>
    public ProjectBackupService(IOptions<StorageOptions> options, Func<string, long> freeBytes)
    {
        databasePath = Path.GetFullPath(options.Value.DatabasePath);
        outputRoot = Path.GetFullPath(options.Value.OutputRoot);
        this.freeBytes = freeBytes;
    }

    // Writes a backup zip to the destination stream. The database is snapshotted through SQLite's
    // online-backup API so the copy is consistent even while the worker is mid-write.
    public async Task ExportAsync(Stream destination, CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException($"No Folio database found at '{databasePath}' to back up.", databasePath);

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

    // Restores a backup zip over the live project, without ever leaving it half replaced (error-handling §16).
    // Extract and validate into staging; check there is room for the new project beside the old; snapshot the
    // live database; copy the new output tree next to the live one; then swap the folders and the database, and
    // put the snapshot and the old folder back if the swap fails. A malformed archive, too little space or a
    // failed copy all stop before anything live is touched.
    public async Task ImportAsync(Stream source, CancellationToken cancellationToken)
    {
        var staging = Path.Combine(Path.GetTempPath(), $"storyboarder-restore-{Guid.NewGuid():N}");
        var restoring = outputRoot.TrimEnd(Path.DirectorySeparatorChar) + ".restoring";
        var retired = outputRoot.TrimEnd(Path.DirectorySeparatorChar) + ".before-restore";
        var snapshot = databasePath + ".before-restore";
        Directory.CreateDirectory(staging);
        try
        {
            await ExtractArchive(source, staging, cancellationToken);

            var stagedDatabase = DatabaseEntryNames
                .Select(name => Path.Combine(staging, name))
                .FirstOrDefault(File.Exists);
            if (stagedDatabase is null)
                throw new InvalidDataException("This file is not a Folio backup: it contains no database.");
            ValidateSqlite(stagedDatabase);

            var stagedOutput = Path.Combine(staging, "output");
            EnsureRoom(stagedDatabase, stagedOutput);

            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(outputRoot)!);

            // The new output, complete, beside the live one. Nothing live has been touched yet.
            if (File.Exists(restoring))
                throw new IOException($"Folio could not restore: '{restoring}' is in the way.");
            if (Directory.Exists(restoring))
                Directory.Delete(restoring, recursive: true);
            if (Directory.Exists(stagedOutput))
                CopyDirectory(stagedOutput, restoring);
            else
                Directory.CreateDirectory(restoring);

            // Release any pooled handles the live process holds on the database before replacing it.
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                SnapshotDatabase(snapshot);
            // The snapshot's own connection is pooled too, and a pooled handle on the live file reads its old
            // pages after the copy below.
            SqliteConnection.ClearAllPools();

            Swap(stagedDatabase, restoring, retired, snapshot);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            if (Directory.Exists(restoring))
                Directory.Delete(restoring, recursive: true);
            if (Directory.Exists(retired))
                Directory.Delete(retired, recursive: true);
            if (File.Exists(snapshot))
                File.Delete(snapshot);
        }
    }

    // The two renames are on one volume, so they are quick and do not need the space twice; the database copy is
    // the one step that can fail on a full disk, and the snapshot is what puts it back.
    private void Swap(string stagedDatabase, string restoring, string retired, string snapshot)
    {
        var outputMoved = false;
        try
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Move(outputRoot, retired);
                outputMoved = true;
            }
            Directory.Move(restoring, outputRoot);

            File.Copy(stagedDatabase, databasePath, overwrite: true);
            // Drop stale journal sidecars so the restored database isn't read through an old journal.
            foreach (var sidecar in new[] { databasePath + "-wal", databasePath + "-shm", databasePath + "-journal" })
                if (File.Exists(sidecar))
                    File.Delete(sidecar);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(snapshot))
                File.Copy(snapshot, databasePath, overwrite: true);
            if (outputMoved)
            {
                if (Directory.Exists(outputRoot))
                    Directory.Delete(outputRoot, recursive: true);
                Directory.Move(retired, outputRoot);
            }
            throw;
        }
    }

    // Room for the new database and output beside the old, plus a database snapshot, with a margin.
    private void EnsureRoom(string stagedDatabase, string stagedOutput)
    {
        var needed = new FileInfo(stagedDatabase).Length
            + (Directory.Exists(stagedOutput) ? Directory.EnumerateFiles(stagedOutput, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) : 0)
            + (File.Exists(databasePath) ? new FileInfo(databasePath).Length : 0);
        needed += needed / 10;

        var free = freeBytes(Path.GetDirectoryName(outputRoot)!);
        if (free < needed)
            throw new IOException(
                $"There isn't enough free space to restore: it needs about {needed / (1024 * 1024) + 1} MB and the phone has {free / (1024 * 1024)} MB. Nothing was changed.");
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
            throw new InvalidDataException("This file is not a readable Folio backup (the archive could not be opened).", ex);
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
