using System.Net;
using Storyworthtelling.Voice.Secrets;
using Storyworthtelling.Voice.Tests.Support;

namespace Storyworthtelling.Voice.Tests;

// Error-handling audit §1 (critical). Google's downloader does not throw on a 401/403/429/5xx or a dropped
// connection; it reports the failure in the progress it returns. DriveTextFile ignored that, so a failed read
// came back as "" — which the vault reads as Empty, and an Empty vault is one a settings save overwrites with
// a single app's section. A failed read must fail; only a file that is not there is null.
public class DriveTextFileReadTests
{
    private const string Name = "storyworthtelling-secrets.json";

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AFailedDownloadThrowsRatherThanReadingAsEmpty(HttpStatusCode status)
    {
        var drive = new FakeDriveHandler();
        drive.Add(Name, null, "{\"version\":1}");
        drive.MediaFailure = status;
        var file = new DriveTextFile(_ => Task.FromResult(drive.Service()), Name);

        var thrown = await Assert.ThrowsAsync<IOException>(() => file.ReadAsync(CancellationToken.None));

        Assert.Contains(Name, thrown.Message, StringComparison.Ordinal);
        Assert.NotNull(thrown.InnerException);
    }

    [Fact]
    public async Task AFileThatIsNotThereIsNull()
    {
        var drive = new FakeDriveHandler();
        var file = new DriveTextFile(_ => Task.FromResult(drive.Service()), Name);

        Assert.Null(await file.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AFileThatIsThereIsReadWhole()
    {
        var drive = new FakeDriveHandler();
        drive.Add(Name, null, "{\"version\":1}");
        var file = new DriveTextFile(_ => Task.FromResult(drive.Service()), Name);

        Assert.Equal("{\"version\":1}", await file.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public void AFileThatIsThereButEmptyIsCorruptNotEmpty()
    {
        // Empty means "no vault yet", which permits creating one. A file that exists and reads back
        // with nothing in it is a damaged vault, and writing a fresh one over it would lose every section.
        Assert.Equal(SecretsVaultStatus.Empty, VaultEnvelope.Read(null, "p").Status);
        Assert.Equal(SecretsVaultStatus.Corrupt, VaultEnvelope.Read(string.Empty, "p").Status);
        Assert.Equal(SecretsVaultStatus.Corrupt, VaultEnvelope.Read("  \n", "p").Status);
    }
}
