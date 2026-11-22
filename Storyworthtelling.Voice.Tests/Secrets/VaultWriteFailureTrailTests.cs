using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

/// <summary>
/// A vault publish that failed at the write said only "WriteFailed (…VaultWriteException)": the exception dropped what
/// Drive threw and the trail dropped which way the write failed (refused, conflict, not read back, the upload itself).
/// The exception now carries Drive's own as its inner, and the trail names the write's outcome word — never a message.
/// </summary>
public class VaultWriteFailureTrailTests
{
    private const string Pass = "example-passphrase";
    private const string DriveMessage = "Upload refused for https://example.com/upload?token=example-secret";

    // A file this account owns whose reads succeed and whose writes throw what the Drive client throws.
    private sealed class WriteThrowingFile : IDriveTextFile, IDriveFileOwnership
    {
        public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task WriteAsync(string content, CancellationToken cancellationToken) =>
            Task.FromException(new HttpRequestException(DriveMessage));

        public Task<DriveFileOwnership> OwnershipAsync(CancellationToken cancellationToken) =>
            Task.FromResult(DriveFileOwnership.Owned);
    }

    // A file another phone keeps writing: its revision moves on every ask, so every write is a conflict.
    private sealed class AlwaysMovingFile : IDriveTextFile, IDriveFileOwnership, IDriveFileRevision
    {
        private int revision;

        public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task WriteAsync(string content, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DriveFileOwnership> OwnershipAsync(CancellationToken cancellationToken) =>
            Task.FromResult(DriveFileOwnership.Owned);

        public Task<string?> RevisionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>("r" + Interlocked.Increment(ref revision));
    }

    [Fact]
    public async Task AFailedUploadKeepsDrivesExceptionAsTheInner()
    {
        var store = new DriveSecretsVaultStore(new WriteThrowingFile());

        var thrown = await Assert.ThrowsAsync<VaultWriteException>(() => store.SaveAsync(SecretsVault.Empty, Pass, default));

        Assert.Equal(SealedWriteOutcome.Failed, thrown.Result.Outcome);
        var inner = Assert.IsType<HttpRequestException>(thrown.InnerException);
        Assert.Equal(DriveMessage, inner.Message);
    }

    [Fact]
    public async Task AFailedPersonalUploadKeepsDrivesExceptionAsTheInner()
    {
        var store = new DrivePersonalVaultStore(new WriteThrowingFile());

        var thrown = await Assert.ThrowsAsync<VaultWriteException>(() => store.SaveAsync(PersonalVault.Empty, Pass, default));

        Assert.IsType<HttpRequestException>(thrown.InnerException);
    }

    [Fact]
    public async Task ASharedPublishThatFailedAtTheUploadSaysSoByWordAndType()
    {
        var lines = new List<(bool Problem, string Line)>();
        var service = new SecretsVaultService(new DriveSecretsVaultStore(new WriteThrowingFile()), () => Pass, (p, l) => lines.Add((p, l)));

        await service.PublishAsync(v => v, default);

        var (problem, line) = Assert.Single(lines);
        Assert.True(problem);
        Assert.Equal(
            "VAULT shared publish: WriteFailed [Failed] (Storyworthtelling.Voice.Secrets.VaultWriteException < System.Net.Http.HttpRequestException)",
            line);
        Assert.DoesNotContain("example", line);
    }

    [Fact]
    public async Task ASharedPublishThatKeptConflictingSaysConflict()
    {
        var lines = new List<(bool Problem, string Line)>();
        var service = new SecretsVaultService(new DriveSecretsVaultStore(new AlwaysMovingFile()), () => Pass, (p, l) => lines.Add((p, l)));

        await service.PublishAsync(v => v, default);

        var (_, line) = Assert.Single(lines);
        Assert.Equal("VAULT shared publish: WriteFailed [Conflict] (Storyworthtelling.Voice.Secrets.VaultWriteException)", line);
    }

    [Fact]
    public async Task APersonalPublishThatFailedAtTheUploadSaysSoByWordAndType()
    {
        var lines = new List<(bool Problem, string Line)>();
        var access = new PersonalVaultAccess(new DrivePersonalVaultStore(new WriteThrowingFile()), () => Pass, (p, l) => lines.Add((p, l)));

        Assert.False(await access.PublishAsync(v => v, CancellationToken.None));

        Assert.Equal(
            [(true, "VAULT personal publish: WriteFailed [Failed] (Storyworthtelling.Voice.Secrets.VaultWriteException < System.Net.Http.HttpRequestException)")],
            lines);
    }
}
