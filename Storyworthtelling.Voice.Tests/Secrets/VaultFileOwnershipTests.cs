using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

public class VaultFileOwnershipTests
{
    private sealed class ReportingFile(DriveFileOwnership ownership) : IDriveTextFile, IDriveFileOwnership
    {
        public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task WriteAsync(string content, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<DriveFileOwnership> OwnershipAsync(CancellationToken cancellationToken) => Task.FromResult(ownership);
    }

    // A file that cannot say whose it is must never be claimable, so it is treated as shared.
    private sealed class SilentFile : IDriveTextFile
    {
        public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task WriteAsync(string content, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task AnOwnedFileIsReportedAsOwned()
    {
        var result = await new DriveSecretsVaultStore(new ReportingFile(DriveFileOwnership.Owned))
            .LoadAsync("pass", default);

        Assert.Equal(DriveFileOwnership.Owned, result.FileOwnership);
    }

    [Fact]
    public async Task ASharedFileIsReportedAsShared()
    {
        var result = await new DriveSecretsVaultStore(new ReportingFile(DriveFileOwnership.Shared))
            .LoadAsync("pass", default);

        Assert.Equal(DriveFileOwnership.Shared, result.FileOwnership);
    }

    [Fact]
    public async Task AFileThatCannotReportOwnershipIsSharedNotClaimable()
    {
        var result = await new DriveSecretsVaultStore(new SilentFile()).LoadAsync("pass", default);

        Assert.Equal(DriveFileOwnership.Shared, result.FileOwnership);
        Assert.Equal(SharedVaultAccess.ReadOnly, VaultOwnership.Decide(result.FileOwnership));
    }

    [Fact]
    public async Task NoFileYetReportsNoneSoTheFirstInstallCanCreateIt()
    {
        var result = await new DriveSecretsVaultStore(new ReportingFile(DriveFileOwnership.None))
            .LoadAsync("pass", default);

        Assert.Equal(SecretsVaultStatus.Empty, result.Status);
        Assert.Equal(SharedVaultAccess.Claimable, VaultOwnership.Decide(result.FileOwnership));
    }
}
