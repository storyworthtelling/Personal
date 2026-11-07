using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice.Tests.Secrets;

// The vault on the sealed-file rule (error-handling §1, contracts M6): a publish that cannot read the vault
// writes nothing, and one that finds another app wrote the vault since it was read merges into that, not
// over it.
public class VaultPublishSealedTests
{
    private const string Pass = "invented sample passphrase";

    [Fact]
    public async Task AVaultDriveCouldNotReadIsNeverWrittenOver()
    {
        var drive = new OwnedDrive();
        await Seed(drive, SecretsVault.Empty with { Claude = new ClaudeSecrets("sk-kept") });
        var before = drive.Inner.Content;
        drive.Inner.FailReads = true;

        var result = await Service(drive).PublishAsync(v => v with { Gemini = new GeminiSecrets("g") }, default);

        Assert.Equal(SecretsVaultPublishStatus.WriteFailed, result.Status);
        Assert.Equal(before, drive.Inner.Content);
    }

    [Fact]
    public async Task AnotherAppsWriteInBetweenIsMergedNotLost()
    {
        var drive = new OwnedDrive();
        await Seed(drive, SecretsVault.Empty with { Claude = new ClaudeSecrets("sk-first") });

        // The other app's write lands after this publish has read the vault, once.
        var store = new DriveSecretsVaultStore(drive);
        var interfering = new InterferingStore(store, async () =>
            await new DriveSecretsVaultStore(drive).SaveAsync(
                (await store.LoadAsync(Pass, default)).Vault with { Remarkable = new RemarkableSecrets("rm-from-other-app") }, Pass, default));

        var result = await new SecretsVaultService(interfering, () => Pass)
            .PublishAsync(v => v with { Gemini = new GeminiSecrets("gemini-this-app") }, default);

        var vault = (await store.LoadAsync(Pass, default)).Vault;
        Assert.Equal(SecretsVaultPublishStatus.Published, result.Status);
        Assert.Equal("rm-from-other-app", vault.Remarkable.DeviceToken);
        Assert.Equal("gemini-this-app", vault.Gemini.ApiKey);
        Assert.Equal("sk-first", vault.Claude.ApiKey);
    }

    private static SecretsVaultService Service(OwnedDrive drive) => new(new DriveSecretsVaultStore(drive), () => Pass);

    private static Task Seed(OwnedDrive drive, SecretsVault vault) => new DriveSecretsVaultStore(drive).SaveAsync(vault, Pass, default);

    /// <summary>Runs <c>interfere</c> once, after the first load and before the first save.</summary>
    private sealed class InterferingStore(ISecretsVaultStore inner, Func<Task> interfere) : ISecretsVaultStore
    {
        private bool done;

        public Task<SecretsVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken) =>
            inner.LoadAsync(passphrase, cancellationToken);

        public Task SaveAsync(SecretsVault vault, string passphrase, CancellationToken cancellationToken) =>
            inner.SaveAsync(vault, passphrase, cancellationToken);

        public async Task SaveAsync(SecretsVault vault, string passphrase, SecretsVaultLoadResult basis, CancellationToken cancellationToken)
        {
            if (!done)
            {
                done = true;
                await interfere();
            }
            await inner.SaveAsync(vault, passphrase, basis, cancellationToken);
        }
    }

    /// <summary>The sealed-file test's drive, owned by this account so the vault may be written.</summary>
    internal sealed class OwnedDrive : IDriveTextFile, IDriveFileRevision, IDriveFileTrash, IDriveFileOwnership
    {
        public SealedDriveFileTests.MemoryDrive Inner { get; } = new();

        public Task<string?> ReadAsync(CancellationToken cancellationToken) => Inner.ReadAsync(cancellationToken);
        public Task WriteAsync(string content, CancellationToken cancellationToken) => Inner.WriteAsync(content, cancellationToken);
        public Task<string?> RevisionAsync(CancellationToken cancellationToken) => Inner.RevisionAsync(cancellationToken);
        public Task TrashAsync(CancellationToken cancellationToken) => Inner.TrashAsync(cancellationToken);
        public Task<DriveFileOwnership> OwnershipAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Inner.Content is null ? DriveFileOwnership.None : DriveFileOwnership.Owned);
    }
}
