using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Logs;
using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice.Tests.Logs;

// The worker's body (plan §3.3): carries LogUploadPlan out against a Drive folder. The loop's one promise
// is that a log leaves the phone only once Drive holds a copy that decrypts to the same content.
public sealed class LogUploaderTests : IDisposable
{
    private const string Passphrase = "an invented passphrase for tests";
    private static readonly LogInstall Install = new("post", "a1b2c3d4e5f6", LogPhone.Owner, "1.0.42", 200042);

    private readonly string files = Path.Combine(Path.GetTempPath(), "swt-upload-" + Guid.NewGuid().ToString("N"));

    public LogUploaderTests() => Directory.CreateDirectory(files);

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

    private string Live(string name, string content)
    {
        var path = Path.Combine(files, name);
        File.WriteAllText(path, content);
        return path;
    }

    private LogUploader Uploader(IDriveLogFolder folder) => new(folder, new LogOutbox(files), Install);

    [Fact]
    public async Task NoPassphraseUploadsNothingAndLeavesTheLiveLogAlone()
    {
        var live = Live("diagnostic.log", "a failure\n");
        var folder = new MemoryLogFolder();

        var result = await Uploader(folder).RunAsync([live], passphrase: "", CancellationToken.None);

        Assert.Equal(LogUploadRefusal.WaitingForPassphrase, result.Refusal);
        Assert.Empty(folder.Files);
        Assert.True(File.Exists(live));
        Assert.Equal("Logs are waiting for the vault passphrase.", result.StatusSentence(0));
    }

    [Fact]
    public async Task AConfirmedUploadClearsTheLogFromThePhone()
    {
        var live = Live("diagnostic.log", "a failure\n");
        var folder = new MemoryLogFolder();
        var outbox = new LogOutbox(files);

        var result = await new LogUploader(folder, outbox, Install).RunAsync([live], Passphrase, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Confirmed);
        Assert.False(File.Exists(live));
        Assert.Empty(outbox.Segments());
        var (name, content) = Assert.Single(folder.Files);
        Assert.Equal("swt-log.post.a1b2c3d4e5f6.000001-000001.json", name);
        Assert.DoesNotContain("a failure", content);
    }

    [Fact]
    public async Task WhatIsUploadedIsSealedWithTheDerivedLogKeyNotThePassphrase()
    {
        var live = Live("diagnostic.log", "a failure\n");
        var folder = new MemoryLogFolder();

        await Uploader(folder).RunAsync([live], Passphrase, CancellationToken.None);

        var content = folder.Files.Single().Value;
        Assert.Equal(SecretsVaultStatus.WrongPassphrase, VaultEnvelope.Read(content, Passphrase).Status);
        var (status, plaintext) = VaultEnvelope.Read(content, LogKey.Derive(Passphrase, LogKey.Epoch));
        Assert.Equal(SecretsVaultStatus.Loaded, status);
        var batch = LogBatchCodec.Decode(plaintext!);
        Assert.Equal("a failure\n", batch.Parts.Single().Text);
        Assert.Equal("post", batch.Header.App);
    }

    [Fact]
    public async Task AReadBackThatDoesNotMatchKeepsEverything()
    {
        var live = Live("diagnostic.log", "a failure\n");
        var folder = new MemoryLogFolder { CorruptOnRead = true };
        var outbox = new LogOutbox(files);

        var result = await new LogUploader(folder, outbox, Install).RunAsync([live], Passphrase, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Confirmed);
        Assert.Single(outbox.Segments());
    }

    [Fact]
    public async Task AFailedCreateKeepsEverythingAndSaysWhy()
    {
        var live = Live("diagnostic.log", "a failure\n");
        var folder = new MemoryLogFolder { FailCreate = true };
        var outbox = new LogOutbox(files);

        var result = await new LogUploader(folder, outbox, Install).RunAsync([live], Passphrase, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Single(outbox.Segments());
        Assert.Contains("network down", result.Error);
    }

    [Fact]
    public async Task ACrashBetweenUploadAndDeleteIsFinishedWithoutASecondCopy()
    {
        var live = Live("diagnostic.log", "a failure\n");
        var folder = new MemoryLogFolder();
        var outbox = new LogOutbox(files);
        await new LogUploader(folder, outbox, Install).RunAsync([live], Passphrase, CancellationToken.None);

        // Put the segment back exactly as it was (content and seal time), as if the delete never happened.
        var (_, plaintext) = VaultEnvelope.Read(folder.Files.Single().Value, LogKey.Derive(Passphrase, LogKey.Epoch));
        var restored = Path.Combine(outbox.Directory, "000001.diagnostic.log.sealed");
        File.WriteAllText(restored, "a failure\n");
        File.SetLastWriteTimeUtc(restored, LogBatchCodec.Decode(plaintext!).Header.LastUtc.UtcDateTime);
        var again = await new LogUploader(folder, outbox, Install).RunAsync([], Passphrase, CancellationToken.None);

        Assert.True(again.Succeeded);
        Assert.Single(folder.Files);
        Assert.Empty(outbox.Segments());
    }

    [Fact]
    public async Task ADifferentFileUnderTheSameNameGetsR2AndIsNotOverwritten()
    {
        var live = Live("diagnostic.log", "a failure\n");
        var folder = new MemoryLogFolder();
        folder.Files["swt-log.post.a1b2c3d4e5f6.000001-000001.json"] = "something else entirely";

        var result = await Uploader(folder).RunAsync([live], Passphrase, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("something else entirely", folder.Files["swt-log.post.a1b2c3d4e5f6.000001-000001.json"]);
        Assert.True(folder.Files.ContainsKey("swt-log.post.a1b2c3d4e5f6.000001-000001.r2.json"));
    }

    [Fact]
    public async Task SealsEveryLiveLogNamedAndUploadsThemTogether()
    {
        var failures = Live("diagnostic.log", "a failure\n");
        var texts = Live("texts.log", "a text arrived\n");
        var folder = new MemoryLogFolder();

        await Uploader(folder).RunAsync([failures, texts], Passphrase, CancellationToken.None);

        var (_, plaintext) = VaultEnvelope.Read(folder.Files.Single().Value, LogKey.Derive(Passphrase, LogKey.Epoch));
        var batch = LogBatchCodec.Decode(plaintext!);
        Assert.Equal(["diagnostic.log", "texts.log"], batch.Parts.Select(p => p.Log));
    }

    // An in-memory folder keyed by name, so ids are names. Optionally corrupts read-backs or fails creates.
    private sealed class MemoryLogFolder : IDriveLogFolder
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
        public bool CorruptOnRead { get; init; }
        public bool FailCreate { get; init; }

        public Task<string?> FindAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(Files.ContainsKey(name) ? name : null);

        public Task<string> CreateAsync(string name, string content, CancellationToken cancellationToken)
        {
            if (FailCreate)
            {
                throw new HttpRequestException("network down");
            }
            Files.Add(name, content);
            return Task.FromResult(name);
        }

        public Task<string?> ReadAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Files.TryGetValue(id, out var content) ? (CorruptOnRead ? content[..^10] + "AAAAAAAA\"}" : content) : null);

        public Task<IReadOnlyList<string>> ProcessedNamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task TrashOwnAsync(string name, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
