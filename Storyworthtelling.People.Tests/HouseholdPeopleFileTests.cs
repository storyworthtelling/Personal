using Storyworthtelling.Household;
using Storyworthtelling.Voice;

namespace Storyworthtelling.People.Tests;

/// <summary>The household file on one phone: the phone's copy is what screens read and edits land in; a pass meets Drive.</summary>
public class HouseholdPeopleFileTests : IDisposable
{
    private const string Passphrase = "invented passphrase for tests";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "people-file-" + Guid.NewGuid().ToString("N"));
    private readonly FakeDrive drive = new();

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private HouseholdPeopleFile File(
        HouseholdFileRole role = HouseholdFileRole.Owner, bool passphrase = true, bool account = true, string? subfolder = null, FakeDrive? shared = null,
        PeopleFileAccess access = PeopleFileAccess.Owner)
    {
        var where = Path.Combine(folder, subfolder ?? "owner");
        var store = new DriveHouseholdStore(shared ?? drive, PeopleFile.FileName, () => passphrase ? Passphrase : string.Empty, () => role == HouseholdFileRole.Owner);
        return new HouseholdPeopleFile(
            new LocalHouseholdStore(where, "people.json"),
            store,
            new PeopleInstallStore(where),
            new PeopleFileSeams(() => passphrase, () => account, () => role, () => new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)),
            access);
    }

    [Fact]
    public async Task AReaderAppCannotEditEvenOnTheOwnersPhone()
    {
        var reader = File(subfolder: "constitution", access: PeopleFileAccess.Reader);

        var edit = await reader.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Sam(), next(), writer), CancellationToken.None);

        Assert.False(reader.CanEdit);
        Assert.False(edit.Accepted);
        Assert.Empty(reader.People.People);
    }

    [Fact]
    public async Task AReaderAppNeverCreatesTheFile()
    {
        var reader = File(subfolder: "constitution", access: PeopleFileAccess.Reader);

        var standing = await reader.SyncAsync(CancellationToken.None);

        Assert.Equal(PeopleFileState.WaitingForShare, standing.State);
        Assert.Null(drive.Content);
    }

    [Fact]
    public async Task AReaderAppTakesForewordsCopyAndWritesNothingBack()
    {
        var foreword = File();
        await foreword.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Sam(), next(), writer), CancellationToken.None);
        await foreword.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Lee(), next(), writer), CancellationToken.None);
        await foreword.SyncAsync(CancellationToken.None);
        var reader = File(subfolder: "constitution", access: PeopleFileAccess.Reader);
        await reader.SyncAsync(CancellationToken.None);

        // Foreword removes Lee; the reader's copy still has Lee, and must neither keep Lee nor send Lee back.
        await foreword.EditAsync((editor, document, next, writer) => editor.Remove(document, "lee", next(), writer), CancellationToken.None);
        await foreword.SyncAsync(CancellationToken.None);
        var written = drive.Content;
        var standing = await reader.SyncAsync(CancellationToken.None);

        Assert.Equal(PeopleFileState.Synced, standing.State);
        Assert.Equal(["Sam"], reader.People.People.Select(person => person.Name));
        Assert.Equal(written, drive.Content);
    }

    [Fact]
    public async Task AReaderAppsStaleCopyNeverReachesDrive()
    {
        // A copy this phone kept from before (an old import) holds a person Foreword never had.
        var stale = File(subfolder: "constitution");
        await stale.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Robin(), next(), writer), CancellationToken.None);
        var foreword = File();
        await foreword.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Sam(), next(), writer), CancellationToken.None);
        await foreword.SyncAsync(CancellationToken.None);
        var written = drive.Content;

        var reader = File(subfolder: "constitution", access: PeopleFileAccess.Reader);
        await reader.ReadAsync(CancellationToken.None);
        await reader.SyncAsync(CancellationToken.None);

        Assert.Equal(written, drive.Content);
        Assert.Equal(["Sam"], reader.People.People.Select(person => person.Name));
    }

    [Fact]
    public async Task AnOwnerEditIsReadBackAtOnce()
    {
        var file = File();

        var edit = await file.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Sam(), next(), writer), CancellationToken.None);

        Assert.True(edit.Accepted);
        Assert.Equal("Sam", Assert.Single(file.People.People).Name);
    }

    [Fact]
    public async Task ThePartnersPhoneCannotEdit()
    {
        var file = File(HouseholdFileRole.SharedWith);

        var edit = await file.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Sam(), next(), writer), CancellationToken.None);

        Assert.False(edit.Accepted);
        Assert.False(file.CanEdit);
        Assert.Empty(file.People.People);
    }

    [Fact]
    public async Task TheOwnersPassCreatesTheFileAndThePartnerReadsIt()
    {
        var owner = File();
        await owner.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Sam(), next(), writer), CancellationToken.None);

        var settled = await owner.SyncAsync(CancellationToken.None);
        var partner = File(HouseholdFileRole.SharedWith, subfolder: "partner");
        var read = await partner.SyncAsync(CancellationToken.None);

        Assert.Equal(PeopleFileState.Synced, settled.State);
        Assert.Equal(PeopleFileState.Synced, read.State);
        Assert.Equal("Sam", Assert.Single(partner.People.People).Name);
    }

    [Fact]
    public async Task ThePartnerWaitsForTheShare()
    {
        var partner = File(HouseholdFileRole.SharedWith);

        var standing = await partner.SyncAsync(CancellationToken.None);

        Assert.Equal(PeopleFileState.WaitingForShare, standing.State);
        Assert.Null(drive.Content);
    }

    [Fact]
    public async Task NoPassphraseStaysOnThePhone()
    {
        var standing = await File(passphrase: false).SyncAsync(CancellationToken.None);

        Assert.Equal(PeopleFileState.LocalOnly, standing.State);
        Assert.Null(drive.Content);
    }

    [Fact]
    public async Task AFileThatWillNotOpenIsLeftAloneAndEditsAreRefused()
    {
        drive.Content = "{\"not\":\"a sealed file\"}";
        var file = File();

        var standing = await file.SyncAsync(CancellationToken.None);
        var edit = await file.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Sam(), next(), writer), CancellationToken.None);

        Assert.Equal(PeopleFileState.Unreadable, standing.State);
        Assert.False(edit.Accepted);
        Assert.Equal("{\"not\":\"a sealed file\"}", drive.Content);
    }

    [Fact]
    public async Task DriveUnreachableIsOfflineAndKeepsTheEdit()
    {
        var file = File();
        await file.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Sam(), next(), writer), CancellationToken.None);
        drive.Unreachable = new HttpRequestException("invented outage");

        var standing = await file.SyncAsync(CancellationToken.None);

        Assert.Equal(PeopleFileState.Offline, standing.State);
        Assert.Single(file.People.People);
    }

    [Fact]
    public async Task ARestartedPhoneStampsNewerThanBefore()
    {
        var first = File();
        await first.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Sam(), next(), writer), CancellationToken.None);

        var again = File();
        await again.ReadAsync(CancellationToken.None);
        await again.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Sam() with { OnPickup = false }, next(), writer), CancellationToken.None);

        Assert.False(again.People.Find("sam")!.OnPickup);
    }

    [Fact]
    public async Task EveryEditUsesOneInstallId()
    {
        var file = File();
        await file.EditAsync((editor, document, next, writer) => editor.Put(document, Household.Sam(), next(), writer), CancellationToken.None);

        var again = File();

        Assert.Equal(file.InstallId, again.InstallId);
        Assert.False(string.IsNullOrWhiteSpace(file.InstallId));
    }

    private sealed class FakeDrive : IDriveTextFile
    {
        public string? Content { get; set; }

        public Exception? Unreachable { get; set; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken) =>
            Unreachable is { } failure ? Task.FromException<string?>(failure) : Task.FromResult(Content);

        public Task WriteAsync(string content, CancellationToken cancellationToken)
        {
            if (Unreachable is { } failure)
            {
                return Task.FromException(failure);
            }

            Content = content;
            return Task.CompletedTask;
        }
    }
}
