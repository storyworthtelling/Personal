using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

/// <summary>
/// Audit 11 §13: the launch-time step that carries shared key verdicts both ways, between this app's health store and
/// the vault, over the real store and crypto on an in-memory Drive file.
/// </summary>
public class KeyHealthSyncTests
{
    private const string Pass = "example-passphrase";

    private sealed class MemoryFile(DriveFileOwnership ownership) : IDriveTextFile, IDriveFileOwnership
    {
        public string? Content { get; set; }
        public int Writes { get; private set; }
        public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Content);
        public Task WriteAsync(string content, CancellationToken cancellationToken)
        {
            Content = content;
            Writes++;
            return Task.CompletedTask;
        }
        public Task<DriveFileOwnership> OwnershipAsync(CancellationToken cancellationToken) => Task.FromResult(ownership);
    }

    private sealed class MemoryHealth : ICredentialHealthStorage
    {
        private string report = string.Empty;
        public string Read() => report;
        public void Write(string value) => report = value;
    }

    private static async Task<(SecretsVaultService Service, MemoryFile File)> Vault(DriveFileOwnership ownership = DriveFileOwnership.Owned)
    {
        var file = new MemoryFile(DriveFileOwnership.Owned);
        var seed = new SecretsVaultService(new DriveSecretsVaultStore(file), () => Pass);
        await seed.PublishAsync(v => v with { Claude = new ClaudeSecrets("example-claude-key") }, default);
        var shared = new MemoryFile(ownership) { Content = file.Content };
        return (new SecretsVaultService(new DriveSecretsVaultStore(shared), () => Pass), shared);
    }

    [Fact]
    public async Task ARefusalInOneAppReachesAnother()
    {
        var (service, _) = await Vault();
        var post = new CredentialHealthStore(new MemoryHealth());
        post.MarkFailing(SharedCredentialIds.Claude, "invalid x-api-key");
        await service.SyncKeyHealthAsync(post, _ => null, default);

        var folio = new CredentialHealthStore(new MemoryHealth());
        await service.SyncKeyHealthAsync(folio, _ => "example-claude-key", default);

        Assert.Equal(CredentialStatus.Failing, folio.Load().Get(SharedCredentialIds.Claude).Status);
    }

    [Fact]
    public async Task NothingNewWritesNothing()
    {
        var (service, file) = await Vault();
        var health = new CredentialHealthStore(new MemoryHealth());
        health.MarkOk(SharedCredentialIds.Claude);
        await service.SyncKeyHealthAsync(health, _ => null, default);
        var writes = file.Writes;

        await service.SyncKeyHealthAsync(health, _ => null, default);

        Assert.Equal(writes, file.Writes);
    }

    [Fact]
    public async Task APhoneThatOnlyReadsTheVaultStillTakesVerdicts()
    {
        var (owner, ownerFile) = await Vault();
        var post = new CredentialHealthStore(new MemoryHealth());
        post.MarkFailing(SharedCredentialIds.Claude, "401");
        await owner.SyncKeyHealthAsync(post, _ => null, default);

        var partnerFile = new MemoryFile(DriveFileOwnership.Shared) { Content = ownerFile.Content };
        var partner = new SecretsVaultService(new DriveSecretsVaultStore(partnerFile), () => Pass);
        var hers = new CredentialHealthStore(new MemoryHealth());
        await partner.SyncKeyHealthAsync(hers, _ => null, default);

        Assert.Equal(CredentialStatus.Failing, hers.Load().Get(SharedCredentialIds.Claude).Status);
        Assert.Equal(0, partnerFile.Writes);
    }
}
