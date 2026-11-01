using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Logs;

namespace Storyworthtelling.Voice.Tests.Logs;

// The bookkeeping around an upload that every head needs and the MAUI heads share (LOG-LOOP-PLAN.md §3.3): a
// random install id kept once, the upload history that decides whether the Saturday window is still open, the
// waiting-for-a-passphrase flag, and one line in the head's own log saying what the run did.
public sealed class LogLoopRunTests : IDisposable
{
    private const string Passphrase = "an invented passphrase for tests";
    private static readonly DateTimeOffset Saturday = new(2026, 10, 31, 19, 0, 0, TimeSpan.Zero);

    private readonly string files = Path.Combine(Path.GetTempPath(), "swt-looprun-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryState state = new();
    private readonly List<(bool Problem, string Line)> lines = [];

    public LogLoopRunTests() => Directory.CreateDirectory(files);

    public void Dispose()
    {
        try
        {
            Directory.Delete(files, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private LogLoopRun Run() => new(state, (problem, line) => lines.Add((problem, line)), () => Saturday);

    private string Live(string text)
    {
        var path = Path.Combine(files, "refresh.log");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void TheInstallIdIsMadeOnceAndKept()
    {
        var first = Run().Install("index", LogPhone.Owner, "1.0.7", 100007);
        var second = Run().Install("index", LogPhone.Owner, "1.0.7", 100007);

        Assert.Matches("^[0-9a-f]{12}$", first.InstallId);
        Assert.Equal(first.InstallId, second.InstallId);
        Assert.Equal("index", first.App);
    }

    [Fact]
    public async Task AConfirmedRunClosesTheWindowAndSaysSo()
    {
        var run = Run();
        Assert.True(run.Due);

        var result = await run.RunAsync(new MemoryFolder(), new LogOutbox(files), run.Install("index", LogPhone.Owner, "1.0.7", 100007),
            [Live("a line\n")], Passphrase, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(Run().Due);
        Assert.Equal(Saturday, Run().History.LastConfirmed);
        Assert.Contains(lines, l => !l.Problem && l.Line == "Uploaded and confirmed 1 of 1 batches.");
    }

    [Fact]
    public async Task NoPassphraseIsRememberedAsWaitingAndLeavesTheWindowOpen()
    {
        var run = Run();

        await run.RunAsync(new MemoryFolder(), new LogOutbox(files), run.Install("index", LogPhone.Owner, "1.0.7", 100007),
            [Live("a line\n")], passphrase: "", CancellationToken.None);

        Assert.True(state.WaitingForPassphrase);
        Assert.True(Run().Due);
        Assert.Equal("Logs are waiting for the vault passphrase.", Run().Status);
    }

    [Fact]
    public async Task AFailedRunIsAProblemLineAndCountsTheWeek()
    {
        var run = Run();

        var result = await run.RunAsync(new MemoryFolder { FailCreate = true }, new LogOutbox(files),
            run.Install("index", LogPhone.Owner, "1.0.7", 100007), [Live("a line\n")], Passphrase, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(1, Run().History.FailedWeeks);
        Assert.Contains(lines, l => l.Problem && l.Line.StartsWith("Upload did not complete:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AThrowingFolderIsAResultNotAnException()
    {
        var run = Run();

        var result = await run.RunAsync(new MemoryFolder { ThrowOnFind = true }, new LogOutbox(files),
            run.Install("index", LogPhone.Owner, "1.0.7", 100007), [Live("a line\n")], Passphrase, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("LogFolderNotSharedException", result.Error);
    }

    private sealed class MemoryState : ILogLoopState
    {
        public string? InstallId { get; set; }
        public string? UploadHistory { get; set; }
        public bool WaitingForPassphrase { get; set; }
    }

    private sealed class MemoryFolder : IDriveLogFolder
    {
        private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
        public bool FailCreate { get; init; }
        public bool ThrowOnFind { get; init; }

        public Task<string?> FindAsync(string name, CancellationToken cancellationToken) =>
            ThrowOnFind ? throw new LogFolderNotSharedException("storyworthtelling-logs") : Task.FromResult(files.ContainsKey(name) ? name : null);

        public Task<string> CreateAsync(string name, string content, CancellationToken cancellationToken)
        {
            if (FailCreate)
            {
                throw new HttpRequestException("network down");
            }
            files.Add(name, content);
            return Task.FromResult(name);
        }

        public Task<string?> ReadAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(files.TryGetValue(id, out var content) ? content : null);

        public Task<IReadOnlyList<string>> ProcessedNamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task TrashOwnAsync(string name, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
