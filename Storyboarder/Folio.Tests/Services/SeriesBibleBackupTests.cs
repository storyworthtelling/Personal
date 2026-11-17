using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Folio.Domain.Model;
using Folio.Domain.Series;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Services;
using Folio.Tests.Support;
using Storyworthtelling.Paper;
using Xunit;

namespace Folio.Tests.Services;

// The series bible's backup in Drive (owner's request): one plain Google Doc per series, rewritten in place
// each time the bible changes, so its version history shows what each book and each correction changed.
public class SeriesBibleBackupTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Series = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly Mock<IPaperDriveArchive> drive = new();
    private readonly List<(string? FileId, string Folder, string Name, string Text)> writes = [];
    private readonly List<(string? FileId, string Folder, string Name, string Text)> dataWrites = [];

    public SeriesBibleBackupTests()
    {
        drive.Setup(d => d.WriteGoogleDoc(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string? id, string folder, string name, string text, CancellationToken _) =>
            {
                writes.Add((id, folder, name, text));
                return Task.FromResult(id ?? "doc-1");
            });
        drive.Setup(d => d.WriteTextFile(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string? id, string folder, string name, string _, string text, CancellationToken _) =>
            {
                dataWrites.Add((id, folder, name, text));
                return Task.FromResult(id ?? "data-1");
            });
    }

    public void Dispose() => database.Dispose();

    private SeriesBibleBackup Backup(bool archive = true, IPaperDelivery? delivery = null) => new(
        database, delivery ?? new PaperDelivery(drive: drive.Object),
        Options.Create(new ArchiveOptions { Enabled = archive }), clock, NullLogger.Instance);

    private Guid SeedBook(string? folder = "folder-one", string? bibleJson = null)
    {
        using var context = database.NewContext();
        var book = new Novel
        {
            Title = "The Salt Road", GoogleDocId = "doc", SeriesId = Series, SeriesName = "The Tides",
            ArchiveFolderId = folder, Stage = ProjectStage.Completed, CreatedAtUtc = Start, UpdatedAtUtc = Start,
        };
        book.ProjectId = book.Id;
        context.Novels.Add(book);
        context.SeriesBibles.Add(new SeriesBibleRecord
        {
            SeriesId = Series,
            Json = bibleJson ?? SeriesBibleCodec.Write(SeriesBible.Empty.WithBook(new SeriesBook(book.Id, "The Salt Road", 1),
                [new BibleFact(BibleKind.Setting, "Gullhaven", [], "", "On stilts.", "")])),
            UpdatedAtUtc = Start,
        });
        context.SaveChanges();
        return book.Id;
    }

    [Fact]
    public async Task TheFirstBackupMakesTheDocBesideTheBookAndRemembersIt()
    {
        SeedBook();

        Assert.True(await Backup().WriteAsync(Series, "after The Salt Road, draft 1", null, CancellationToken.None));

        var write = Assert.Single(writes);
        Assert.Null(write.FileId);
        Assert.Equal("folder-one", write.Folder);
        Assert.Equal("The Tides — Series bible", write.Name);
        Assert.Contains("This version: after The Salt Road, draft 1", write.Text);
        Assert.Contains("Gullhaven", write.Text);
        using var context = database.NewContext();
        Assert.Equal("doc-1", context.SeriesBibles.Single().DriveDocId);
    }

    /// <summary>
    /// Beside the Doc, the bible's own data is kept as a plain JSON file, rewritten in place, so a new phone
    /// can restore it whole: the Doc is for reading, the data is for restoring.
    /// </summary>
    [Fact]
    public async Task EachBackupAlsoWritesTheBiblesDataBesideTheDoc()
    {
        SeedBook();

        await Backup().WriteAsync(Series, "after The Salt Road, draft 1", null, CancellationToken.None);
        await Backup().WriteAsync(Series, "your correction to Gullhaven", null, CancellationToken.None);

        Assert.Equal("The Tides — Series bible (data).json", dataWrites[0].Name);
        Assert.Equal("folder-one", dataWrites[0].Folder);
        Assert.Null(dataWrites[0].FileId);
        Assert.Equal("data-1", dataWrites[1].FileId);
        Assert.Contains("Gullhaven", SeriesBibleCodec.Read(dataWrites[0].Text).Entries.Single().Name);
        using var context = database.NewContext();
        Assert.Equal("data-1", context.SeriesBibles.Single().DriveDataId);
    }

    // A phone that has the series' books but no bible: what a reset leaves behind.
    private Guid SeedBookWithoutBible(string title = "The Salt Road")
    {
        using var context = database.NewContext();
        var book = new Novel
        {
            Title = title, GoogleDocId = "doc", SeriesId = Series, SeriesName = "The Tides", SeriesSequence = 1,
            ArchiveFolderId = "folder-one", Stage = ProjectStage.Completed, CreatedAtUtc = Start, UpdatedAtUtc = Start,
        };
        book.ProjectId = book.Id;
        context.Novels.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private static string OldPhonesBible()
    {
        var bible = SeriesBible.Empty
            .WithBook(new SeriesBook(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "The Salt Road", 1),
                [new BibleFact(BibleKind.Setting, "Gullhaven", [], "", "On stilts.", "")])
            .Correct(BibleKind.Setting, "Gullhaven", "On piles.");
        return SeriesBibleCodec.Write(bible);
    }

    /// <summary>
    /// After a reset, the bible comes back from Drive: found by its data file's name, rebound to this
    /// phone's books, with the owner's corrections, and the Doc and data remembered so the next backup
    /// rewrites them rather than starting new ones.
    /// </summary>
    [Fact]
    public async Task APhoneWithNoBibleRestoresItFromDrive()
    {
        var book = SeedBookWithoutBible();
        drive.Setup(d => d.FindFileByName("The Tides — Series bible (data).json", It.IsAny<CancellationToken>())).ReturnsAsync("data-9");
        drive.Setup(d => d.FindFileByName("The Tides — Series bible", It.IsAny<CancellationToken>())).ReturnsAsync("doc-9");
        drive.Setup(d => d.ReadTextFile("data-9", It.IsAny<CancellationToken>())).ReturnsAsync(OldPhonesBible());

        Assert.Equal(BibleRestore.Restored, await Backup().RestoreAsync(Series, CancellationToken.None));

        using var context = database.NewContext();
        var record = context.SeriesBibles.Single();
        Assert.Equal("doc-9", record.DriveDocId);
        Assert.Equal("data-9", record.DriveDataId);
        var bible = SeriesBibleCodec.Read(record.Json);
        Assert.True(bible.Has(book));
        Assert.Equal("On piles.", bible.Entries.Single().Current);
    }

    [Fact]
    public async Task APhoneThatHasItsBibleAsksDriveNothing()
    {
        SeedBook();

        Assert.Equal(BibleRestore.NotNeeded, await Backup().RestoreAsync(Series, CancellationToken.None));
        drive.Verify(d => d.FindFileByName(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoCopyInDriveRestoresNothingAndWritesNothing()
    {
        SeedBookWithoutBible();
        drive.Setup(d => d.FindFileByName(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        Assert.Equal(BibleRestore.NotFound, await Backup().RestoreAsync(Series, CancellationToken.None));
        using var context = database.NewContext();
        Assert.Empty(context.SeriesBibles);
    }

    /// <summary>A copy in Drive that will not read is left alone and nothing is written from it.</summary>
    [Fact]
    public async Task ADamagedCopyInDriveIsNotRestored()
    {
        SeedBookWithoutBible();
        drive.Setup(d => d.FindFileByName("The Tides — Series bible (data).json", It.IsAny<CancellationToken>())).ReturnsAsync("data-9");
        drive.Setup(d => d.ReadTextFile("data-9", It.IsAny<CancellationToken>())).ReturnsAsync("not a bible");

        Assert.Equal(BibleRestore.Unreadable, await Backup().RestoreAsync(Series, CancellationToken.None));
        using var context = database.NewContext();
        Assert.Empty(context.SeriesBibles);
    }

    /// <summary>A Drive that will not answer is not a missing bible: nothing is written, and it says so.</summary>
    [Fact]
    public async Task ADriveThatWillNotAnswerRestoresNothing()
    {
        SeedBookWithoutBible();
        drive.Setup(d => d.FindFileByName(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("offline"));

        Assert.Equal(BibleRestore.Unavailable, await Backup().RestoreAsync(Series, CancellationToken.None));
        using var context = database.NewContext();
        Assert.Empty(context.SeriesBibles);
    }

    /// <summary>Every later backup rewrites the same doc by its id, so Drive keeps one version history.</summary>
    [Fact]
    public async Task ALaterBackupRewritesTheSameDoc()
    {
        SeedBook();
        await Backup().WriteAsync(Series, "after The Salt Road, draft 1", null, CancellationToken.None);

        await Backup().WriteAsync(Series, "your correction to Gullhaven", "another-folder", CancellationToken.None);

        Assert.Equal("doc-1", writes[1].FileId);
        Assert.Contains("This version: your correction to Gullhaven", writes[1].Text);
    }

    [Fact]
    public async Task NothingIsWrittenFromABibleThatWillNotRead()
    {
        SeedBook(bibleJson: "damaged");

        Assert.False(await Backup().WriteAsync(Series, "x", null, CancellationToken.None));
        Assert.Empty(writes);
    }

    [Fact]
    public async Task NothingIsWrittenWithoutDriveOrSomewhereToPutIt()
    {
        SeedBook(folder: null);

        Assert.False(await Backup().WriteAsync(Series, "x", null, CancellationToken.None));
        Assert.False(await Backup(archive: false).WriteAsync(Series, "x", "folder", CancellationToken.None));
        Assert.False(await Backup(delivery: new PaperDelivery()).WriteAsync(Series, "x", "folder", CancellationToken.None));
        Assert.Empty(writes);
    }

    /// <summary>A failed write is reported as not written, never thrown: the backup is best-effort.</summary>
    [Fact]
    public async Task AFailedWriteIsReportedNotThrown()
    {
        SeedBook();
        drive.Setup(d => d.WriteGoogleDoc(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));

        Assert.False(await Backup().WriteAsync(Series, "x", null, CancellationToken.None));
        using var context = database.NewContext();
        Assert.Null(context.SeriesBibles.Single().DriveDocId);
    }
}
