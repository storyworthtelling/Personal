using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Secrets;

namespace LogLoopReader.Tests;

/// <summary>Invented batches and a fake Drive listing. Every name, number and address here is made up.</summary>
internal static class Support
{
    public const string Passphrase = "an invented passphrase for the reader tests";
    public static readonly string Key = LogKey.Derive(Passphrase, LogKey.Epoch);

    public static LogBatch Batch(string app, string phone, string text, int day = 30, string log = "diagnostic.log", long sequence = 1) => new(
        new LogBatchHeader(LogBatch.CurrentSchema, app, "a1b2c3d4e5f6", phone, "1.0.42", 200042, LogKey.Epoch,
            new DateTimeOffset(2026, 10, day, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, day, 20, 0, 0, TimeSpan.Zero), [sequence]),
        [new LogBatchPart(sequence, log, text)]);

    public static string Sealed(LogBatch batch, string key) => VaultEnvelope.Write(LogBatchCodec.Encode(batch), key);

    public static string TempOutside()
    {
        var path = Path.Combine(Path.GetTempPath(), "log-loop-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")) && !File.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    // An entry exactly as DiagnosticLogFormatter writes it, so the digest is tested on the real shape.
    public static string Entry(string level, string source, string message, string? detail = null) =>
        $"[Oct 30, 9:15 AM] {level} {source} — {message}" + (detail is null ? string.Empty : "\n" + detail) + "\n\n";

    public sealed class FakeSource(Dictionary<string, string> files) : ILogSource
    {
        public Task<IReadOnlyList<RemoteLogFile>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RemoteLogFile>>(files.Keys.Select(n => new RemoteLogFile("id-" + n, n)).ToList());

        public Task<string> DownloadAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(files[id["id-".Length..]]);
    }
}
