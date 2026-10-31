using Storyworthtelling.Voice.Logs;
using Storyworthtelling.Voice.Tests.Support;

namespace Storyworthtelling.Voice.Tests.Logs;

// The Drive half of the log loop (plan §3.4): one folder, files created by name and never overwritten,
// read back by id, and — for her phone later (§5.6) — the processed list and trashing only its own files.
// Driven through the real DriveService over an in-memory fake, so the requests are the ones a phone sends.
public class DriveLogFolderTests
{
    private const string Folder = "storyworthtelling-logs";

    private static DriveLogFolder Over(FakeDriveHandler drive) =>
        new(_ => Task.FromResult(drive.Service()), Folder);

    [Fact]
    public async Task FindingInAFolderThatDoesNotExistYetIsNullAndCreatesNothing()
    {
        var drive = new FakeDriveHandler();

        var id = await Over(drive).FindAsync("swt-log.post.a.000001-000001.json", CancellationToken.None);

        Assert.Null(id);
        Assert.Empty(drive.Files);
    }

    [Fact]
    public async Task CreatingMakesTheFolderOnceAndPutsTheFileInIt()
    {
        var drive = new FakeDriveHandler();
        var folder = Over(drive);

        var first = await folder.CreateAsync("swt-log.post.a.000001-000001.json", "sealed one", CancellationToken.None);
        var second = await folder.CreateAsync("swt-log.post.a.000002-000002.json", "sealed two", CancellationToken.None);

        var folders = drive.Files.Where(f => f.MimeType == "application/vnd.google-apps.folder").ToList();
        var created = Assert.Single(folders);
        Assert.Equal(Folder, created.Name);
        Assert.All(drive.Files.Where(f => f.MimeType != "application/vnd.google-apps.folder"),
            f => Assert.Equal(created.Id, f.Parent));
        Assert.NotEqual(first, second);
        Assert.Equal("sealed one", drive.Files.Single(f => f.Id == first).Content);
    }

    [Fact]
    public async Task AnExistingFolderIsUsedRatherThanASecondOne()
    {
        var drive = new FakeDriveHandler();
        var existing = drive.AddFolder(Folder);

        await Over(drive).CreateAsync("swt-log.post.a.000001-000001.json", "x", CancellationToken.None);

        Assert.Single(drive.Files, f => f.MimeType == "application/vnd.google-apps.folder");
        Assert.Equal(existing.Id, drive.Files.Single(f => f.Name.EndsWith(".json", StringComparison.Ordinal)).Parent);
    }

    [Fact]
    public async Task FindLooksOnlyInsideTheLogFolder()
    {
        var drive = new FakeDriveHandler();
        var logs = drive.AddFolder(Folder);
        const string name = "swt-log.post.a.000001-000001.json";
        drive.Add(name, parent: "somewhere-else");
        var inside = drive.Add(name, logs.Id);

        var id = await Over(drive).FindAsync(name, CancellationToken.None);

        Assert.Equal(inside.Id, id);
    }

    [Fact]
    public async Task ATrashedFileIsNotFound()
    {
        var drive = new FakeDriveHandler();
        var logs = drive.AddFolder(Folder);
        const string name = "swt-log.post.a.000001-000001.json";
        drive.Add(name, logs.Id).Trashed = true;

        Assert.Null(await Over(drive).FindAsync(name, CancellationToken.None));
    }

    [Fact]
    public async Task ReadByIdReturnsTheContentAndAMissingIdIsNull()
    {
        var drive = new FakeDriveHandler();
        var logs = drive.AddFolder(Folder);
        var file = drive.Add("swt-log.post.a.000001-000001.json", logs.Id, "the ciphertext");
        var folder = Over(drive);

        Assert.Equal("the ciphertext", await folder.ReadAsync(file.Id, CancellationToken.None));
        Assert.Null(await folder.ReadAsync("no-such-id", CancellationToken.None));
    }

    [Fact]
    public async Task TheProcessedListIsReadFromTheFolder()
    {
        var drive = new FakeDriveHandler();
        var logs = drive.AddFolder(Folder);
        drive.Add(DriveLogFolder.ProcessedListName, logs.Id,
            "{\"names\":[\"swt-log.post.b.000001-000002.json\",\"swt-log.post.b.000003-000003.json\"]}");

        var names = await Over(drive).ProcessedNamesAsync(CancellationToken.None);

        Assert.Equal(["swt-log.post.b.000001-000002.json", "swt-log.post.b.000003-000003.json"], names);
    }

    [Fact]
    public async Task NoProcessedListOrAnUnreadableOneIsAnEmptyList()
    {
        var empty = new FakeDriveHandler();
        Assert.Empty(await Over(empty).ProcessedNamesAsync(CancellationToken.None));

        var garbled = new FakeDriveHandler();
        var logs = garbled.AddFolder(Folder);
        garbled.Add(DriveLogFolder.ProcessedListName, logs.Id, "not json");
        Assert.Empty(await Over(garbled).ProcessedNamesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TrashOwnTrashesOnlyAFileThisAccountOwns()
    {
        var drive = new FakeDriveHandler();
        var logs = drive.AddFolder(Folder);
        var mine = drive.Add("swt-log.post.b.000001-000001.json", logs.Id, ownedByMe: true);
        var theirs = drive.Add("swt-log.post.c.000001-000001.json", logs.Id, ownedByMe: false);
        var folder = Over(drive);

        await folder.TrashOwnAsync(mine.Name, CancellationToken.None);
        await folder.TrashOwnAsync(theirs.Name, CancellationToken.None);

        Assert.True(mine.Trashed);
        Assert.False(theirs.Trashed);
        Assert.DoesNotContain(drive.Requests, r => r.StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NothingIsEverDeletedOrOverwritten()
    {
        var drive = new FakeDriveHandler();
        var folder = Over(drive);

        var id = await folder.CreateAsync("swt-log.post.a.000001-000001.json", "x", CancellationToken.None);
        await folder.FindAsync("swt-log.post.a.000001-000001.json", CancellationToken.None);
        await folder.ReadAsync(id, CancellationToken.None);

        Assert.DoesNotContain(drive.Requests, r => r.StartsWith("DELETE", StringComparison.Ordinal));
        Assert.DoesNotContain(drive.Requests, r => r.StartsWith("PATCH", StringComparison.Ordinal));
    }
}
