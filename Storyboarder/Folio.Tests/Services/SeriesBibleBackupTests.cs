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

    public SeriesBibleBackupTests()
    {
        drive.Setup(d => d.WriteGoogleDoc(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string? id, string folder, string name, string text, CancellationToken _) =>
            {
                writes.Add((id, folder, name, text));
                return Task.FromResult(id ?? "doc-1");
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
