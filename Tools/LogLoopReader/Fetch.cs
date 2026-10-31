using System.Text.Json;
using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Secrets;

namespace LogLoopReader;

/// <summary>A file in the log folder, as the listing names it.</summary>
public sealed record RemoteLogFile(string Id, string Name);

/// <summary>Where the week's sealed batches come from: the Drive folder, or a fake in tests.</summary>
public interface ILogSource
{
    Task<IReadOnlyList<RemoteLogFile>> ListAsync(CancellationToken cancellationToken);
    Task<string> DownloadAsync(string id, CancellationToken cancellationToken);
}

public enum FetchStatus
{
    Decrypted,

    /// <summary>The GCM tag failed: another key, or an older epoch. Reported, never trashed.</summary>
    WrongKey,

    /// <summary>Not an envelope, or not a batch inside one. Reported, never trashed.</summary>
    Corrupt,
}

public sealed record FetchedFile(string Name, string Id, FetchStatus Status, string? PlainPath);

/// <summary>Thrown before anything is written when an output path lies inside the repository (§6.1).</summary>
public sealed class OutputInsideRepoException(string path, string repoRoot)
    : Exception($"Refusing to write log plaintext to {path}: it is inside the repository at {repoRoot}. Use a path under {OutputGuard.DefaultOutput()}.");

/// <summary>§6.1 layer 1: plaintext never enters the working tree.</summary>
public static class OutputGuard
{
    public static string DefaultOutput() => Path.Combine(Path.GetFullPath(Path.GetTempPath()), "log-loop");

    public static void EnsureOutside(string output, string repoRoot)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        if (string.Equals(full, root, StringComparison.Ordinal)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new OutputInsideRepoException(full, root);
        }
    }

    /// <summary>The repository containing <paramref name="start"/>, found by its .git entry; null outside one.</summary>
    public static string? FindRepoRoot(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return null;
    }
}

/// <summary>The session's record of each file's progress (§5.4): fetched, decrypted, triaged, done.</summary>
public static class Manifest
{
    public const string FileName = "manifest.json";

    public sealed record Entry(string Name, string Id, string State, string? Status);

    public static void Write(string outputDirectory, IEnumerable<Entry> entries) =>
        File.WriteAllText(Path.Combine(outputDirectory, FileName),
            JsonSerializer.Serialize(entries.OrderBy(e => e.Name, StringComparer.Ordinal), new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>
/// Downloads every <c>swt-log.*.json</c> in the folder and decrypts it with the log key, writing each
/// batch's plaintext under <c>plain/</c> in the output folder — which must lie outside the repository —
/// and a manifest beside it. A file that fails the GCM tag is WrongKey, one that is not an envelope or
/// not a batch is Corrupt; neither is ever misread as an empty week.
/// </summary>
public sealed class FetchCommand(ILogSource source, string logKey)
{
    public const string PlainFolder = "plain";

    public async Task<IReadOnlyList<FetchedFile>> RunAsync(string outputDirectory, string repoRoot, CancellationToken cancellationToken)
    {
        OutputGuard.EnsureOutside(outputDirectory, repoRoot);
        var plain = Path.Combine(outputDirectory, PlainFolder);

        var files = (await source.ListAsync(cancellationToken))
            .Where(f => f.Name.StartsWith(LogUploadPlan.NamePrefix, StringComparison.Ordinal) && f.Name.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        Directory.CreateDirectory(plain);
        var fetched = new List<FetchedFile>();
        foreach (var file in files)
        {
            var content = await source.DownloadAsync(file.Id, cancellationToken);
            var (status, text) = VaultEnvelope.Read(content, logKey);
            if (status == SecretsVaultStatus.WrongPassphrase)
            {
                fetched.Add(new FetchedFile(file.Name, file.Id, FetchStatus.WrongKey, null));
                continue;
            }
            if (status != SecretsVaultStatus.Loaded || text is null)
            {
                fetched.Add(new FetchedFile(file.Name, file.Id, FetchStatus.Corrupt, null));
                continue;
            }
            LogBatch batch;
            try
            {
                batch = LogBatchCodec.Decode(text);
            }
            catch (LogBatchFormatException)
            {
                fetched.Add(new FetchedFile(file.Name, file.Id, FetchStatus.Corrupt, null));
                continue;
            }
            var path = Path.Combine(plain, file.Name);
            File.WriteAllText(path, LogBatchCodec.Json(batch));
            fetched.Add(new FetchedFile(file.Name, file.Id, FetchStatus.Decrypted, path));
        }

        Manifest.Write(outputDirectory, fetched.Select(f => new Manifest.Entry(
            f.Name, f.Id, f.Status == FetchStatus.Decrypted ? "decrypted" : "fetched", f.Status.ToString())));
        return fetched;
    }

    /// <summary>A plaintext batch written by <see cref="RunAsync"/>, read back.</summary>
    public static LogBatch ReadPlain(string path) => LogBatchCodec.FromJson(File.ReadAllText(path));
}
