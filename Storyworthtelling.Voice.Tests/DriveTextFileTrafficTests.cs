using Storyworthtelling.Voice.Secrets;
using Storyworthtelling.Voice.Tests.Support;

namespace Storyworthtelling.Voice.Tests;

// Efficiency audit §3: every vault read cost about six Drive requests and a full download, with nothing kept.
// Asking about a remembered file is now one request, and a file whose revision has not moved is not downloaded
// again by anything that shares the read cache.
public class DriveTextFileTrafficTests
{
    private const string Name = "storyworthtelling-secrets.json";

    private readonly FakeDriveHandler drive = new();
    private readonly InMemoryDriveFileIdStore ids = new();
    private readonly DriveReadCache cache = new();

    private DriveTextFile File() => new(_ => Task.FromResult(drive.Service()), Name, ids, readCache: cache);

    private int Downloads() => drive.Requests.Count(request => request.Contains("alt=media", StringComparison.Ordinal));

    [Fact]
    public async Task AnUnchangedFileIsDownloadedOnce()
    {
        drive.Add(Name, null, "{\"version\":1}");

        Assert.Equal("{\"version\":1}", await File().ReadAsync(default));
        Assert.Equal("{\"version\":1}", await File().ReadAsync(default));

        Assert.Equal(1, Downloads());
    }

    [Fact]
    public async Task AChangedFileIsDownloadedAgain()
    {
        var file = drive.Add(Name, null, "{\"version\":1}");
        await File().ReadAsync(default);

        file.Content = "{\"version\":2}";

        Assert.Equal("{\"version\":2}", await File().ReadAsync(default));
        Assert.Equal(2, Downloads());
    }

    [Fact]
    public async Task WithoutACacheEveryReadDownloads()
    {
        drive.Add(Name, null, "{\"version\":1}");
        var uncached = new DriveTextFile(_ => Task.FromResult(drive.Service()), Name, ids);

        await uncached.ReadAsync(default);
        await uncached.ReadAsync(default);

        Assert.Equal(2, Downloads());
    }

    [Fact]
    public async Task ARememberedFileTakesOneRequestToAskAbout()
    {
        drive.Add(Name, null, "{}");
        await File().ReadAsync(default);
        drive.Requests.Clear();

        await File().OwnershipAsync(default);
        await File().RevisionAsync(default);

        Assert.Equal(2, drive.Requests.Count);
    }

    [Fact]
    public async Task AWriteIsReadBackFromDriveNotFromTheCache()
    {
        var file = drive.Add(Name, null, "{\"version\":1}");
        await File().ReadAsync(default);

        await File().WriteAsync("{\"version\":2}", default);

        Assert.Equal("{\"version\":2}", file.Content);
        Assert.Equal("{\"version\":2}", await File().ReadAsync(default));
    }

    [Fact]
    public async Task AVaultLoadOfAnUnchangedVaultDownloadsNothing()
    {
        drive.Add(Name, null, VaultEnvelope.Write("{}", "invented passphrase"));
        var store = new DriveSecretsVaultStore(File());
        await store.LoadAsync("invented passphrase", default);
        drive.Requests.Clear();

        var again = await new DriveSecretsVaultStore(File()).LoadAsync("invented passphrase", default);

        Assert.Equal(SecretsVaultStatus.Loaded, again.Status);
        Assert.Equal(0, Downloads());
        Assert.Equal(3, drive.Requests.Count);
    }
}
