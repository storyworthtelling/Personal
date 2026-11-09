using Storyworthtelling.Voice.Secrets;
using Storyworthtelling.Voice.Tests.Support;

namespace Storyworthtelling.Voice.Tests;

// The two Drive operations SealedDriveFile leans on, against the real DriveService over the fake Drive:
// the revision a write is checked against, and taking back a first upload that did not read back.
public class DriveTextFileRevisionTests
{
    private const string Name = "storyworthtelling-secrets.json";
    private const string Key = "invented sample passphrase";

    [Fact]
    public async Task TheRevisionIsNullWithNoFileAndMovesWhenTheFileIsWritten()
    {
        var drive = new FakeDriveHandler();
        var file = new DriveTextFile(_ => Task.FromResult(drive.Service()), Name);

        Assert.Null(await file.RevisionAsync(default));

        await file.WriteAsync("one", default);
        var first = await file.RevisionAsync(default);
        await file.WriteAsync("two", default);
        var second = await file.RevisionAsync(default);

        Assert.NotNull(first);
        Assert.NotEqual(first, second);
        Assert.Equal("two", await file.ReadAsync(default));
    }

    [Fact]
    public async Task TrashMovesTheFileOutOfSight()
    {
        var drive = new FakeDriveHandler();
        drive.Add(Name, null, "x");
        var file = new DriveTextFile(_ => Task.FromResult(drive.Service()), Name);

        await file.TrashAsync(default);

        Assert.True(drive.Files.Single().Trashed);
        Assert.Null(await file.ReadAsync(default));
    }

    [Fact]
    public async Task AFileSharedWithThisAccountIsNeverTrashedByIt()
    {
        var drive = new FakeDriveHandler();
        drive.Add(Name, null, "x", ownedByMe: false);
        var file = new DriveTextFile(_ => Task.FromResult(drive.Service()), Name);

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.TrashAsync(default));
        Assert.False(drive.Files.Single().Trashed);
    }

    [Fact]
    public async Task ASealedWriteOverDriveSeesAnotherPhonesWriteAsAConflict()
    {
        var drive = new FakeDriveHandler();
        drive.Add(Name, null, VaultEnvelope.Write("{\"a\":1}", Key));
        var sealedFile = new SealedDriveFile(new DriveTextFile(_ => Task.FromResult(drive.Service()), Name));

        var read = await sealedFile.ReadAsync(Key, default);
        drive.Files.Single().Content = VaultEnvelope.Write("{\"a\":2}", Key);

        var written = await sealedFile.WriteAsync("{\"a\":3}", Key, read, SealedWriteOptions.Default, default);
        var again = await sealedFile.WriteAsync("{\"a\":3}", Key, await sealedFile.ReadAsync(Key, default), SealedWriteOptions.Default, default);

        Assert.Equal(SealedWriteOutcome.Conflict, written.Outcome);
        Assert.Equal(SealedWriteOutcome.Written, again.Outcome);
        Assert.Equal("{\"a\":3}", (await sealedFile.ReadAsync(Key, default)).Plaintext);
    }
}
