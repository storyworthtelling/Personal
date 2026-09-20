using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq;
using Folio.Domain.Analysis;
using Folio.Domain.External;
using Folio.Domain.Model;
using Folio.Domain.Readiness;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Imaging;
using Folio.Infrastructure.Services;
using Folio.Tests.Support;
using Xunit;

namespace Folio.Tests.Services;

/// <summary>
/// What the book page reads. It is a read of the whole project — every draft of one book — rather
/// than of one manuscript, because the page's subject is the book and its drafts are its content.
/// </summary>
public class BookReadTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly Mock<IGoogleDocsClient> docs = new();
    private readonly string outputRoot = Path.Combine(
        Path.GetTempPath(), "folio-bookread", Guid.NewGuid().ToString("N"));
    private readonly FileSystemImageStore imageStore;

    public BookReadTests()
    {
        imageStore = new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot }));
    }

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }

    [Fact]
    public async Task ABookCarriesEveryDraftOfItsProjectOldestFirst()
    {
        var first = Seed(draftNumber: 1, words: 71_000);
        var second = Seed(draftNumber: 2, words: 88_400, projectId: first);
        var third = Seed(draftNumber: 3, words: 94_200, projectId: first);

        var book = await Read(second);

        Assert.NotNull(book);
        Assert.Equal([1, 2, 3], book!.Drafts.Select(draft => draft.Number));
        Assert.Equal(94_200, book.Drafts[^1].WordCount);
        Assert.Equal(third, book.LatestDraftId);
    }

    /// <summary>
    /// Opened from any draft, the page is the same book. The shelf hands over whichever draft it
    /// drew and the desk hands over the one in progress; a page that showed only the drafts after
    /// the one it was opened from would be a different book depending on where you came from.
    /// </summary>
    [Fact]
    public async Task OpeningFromAnEarlierDraftReadsTheWholeBook()
    {
        var first = Seed(draftNumber: 1, words: 71_000);
        Seed(draftNumber: 2, words: 88_400, projectId: first);

        var book = await Read(first);

        Assert.Equal(2, book!.Drafts.Count);
    }

    [Fact]
    public async Task ReadinessIsOneReadingPerDraftThatHasAReport()
    {
        var first = Seed(draftNumber: 1, words: 71_000, report: Report(musts: 6, strengths: 1));
        Seed(draftNumber: 2, words: 88_400, projectId: first, report: Report(musts: 3, strengths: 4));

        var book = await Read(first);

        Assert.Equal(2, book!.Readiness.Count);
        Assert.Equal(6, book.Readiness[0].Counts[ReadinessTier.Must]);
        Assert.Equal(3, book.Readiness[1].Counts[ReadinessTier.Must]);
        Assert.Equal(4, book.Readiness[1].Counts[ReadinessTier.Great]);
    }

    /// <summary>
    /// A draft still being read has no report, and the chart must not invent a zero for it: a line
    /// dropping to nothing would say the Musts were fixed when they have not yet been counted.
    /// </summary>
    [Fact]
    public async Task ADraftWithNoReportYetIsNotPlotted()
    {
        var first = Seed(draftNumber: 1, words: 71_000, report: Report(musts: 6, strengths: 1));
        Seed(draftNumber: 2, words: 88_400, projectId: first);

        var book = await Read(first);

        Assert.Equal(2, book!.Drafts.Count);
        Assert.Single(book.Readiness);
        Assert.Equal(1, book.Readiness[0].Number);
    }

    /// <summary>
    /// The page's open action is offered only when there is something to open, and what it opens is
    /// the latest draft's rendered PDF — the report for the draft the author is working on.
    /// </summary>
    [Fact]
    public async Task TheReportIsTheLatestDraftsAndIsOfferedOnlyWhenItExists()
    {
        var first = Seed(draftNumber: 1, words: 71_000, report: Report(musts: 6, strengths: 1), pdf: "one.pdf");
        var second = Seed(draftNumber: 2, words: 88_400, projectId: first,
            report: Report(musts: 3, strengths: 4), pdf: "two.pdf");

        var book = await Read(first);

        Assert.Equal(second, book!.LatestDraftId);
        Assert.True(book.ReportReady);
    }

    [Fact]
    public async Task ABookWithNoRenderedReportOffersNothingToOpen()
    {
        var first = Seed(draftNumber: 1, words: 71_000);

        var book = await Read(first);

        Assert.False(book!.ReportReady);
    }

    /// <summary>
    /// A series is counted in books, not in drafts. Counting drafts would turn one heavily revised
    /// novel into "Book 1 of five".
    /// </summary>
    [Fact]
    public async Task ASeriesIsCountedInBooks()
    {
        var series = Guid.NewGuid();
        var bookOne = Seed(draftNumber: 1, words: 71_000, seriesId: series, sequence: 1);
        Seed(draftNumber: 2, words: 74_000, projectId: bookOne, seriesId: series, sequence: 1);
        Seed(draftNumber: 1, words: 60_000, seriesId: series, sequence: 2);

        var book = await Read(bookOne);

        Assert.True(book!.InSeries);
        Assert.Equal(2, book.SeriesLength);
        Assert.Equal(1, book.SeriesSequence);
    }

    [Fact]
    public async Task AStandaloneBookIsInNoSeries()
    {
        var book = await Read(Seed(draftNumber: 1, words: 71_000));

        Assert.False(book!.InSeries);
        Assert.Equal(0, book.SeriesLength);
    }

    [Fact]
    public async Task AnUnknownBookReadsAsNothing() =>
        Assert.Null(await Read(Guid.NewGuid()));

    private Task<BookRecord?> Read(Guid draftId) =>
        new ProjectService(database, docs.Object, imageStore, clock, Options.Create(new RateLimitOptions()))
            .GetBookAsync(draftId, CancellationToken.None);

    private static BookAnalysisResult Report(int musts, int strengths) =>
        BookAnalysisResult.Empty with
        {
            RevisionPlan = [.. Enumerable.Range(0, musts)
                .Select(i => new RevisionItem("Must", $"Ch. {i + 1}", "Do something", "A pointer"))],
            Verdict = new Verdict(
                [.. Enumerable.Range(0, strengths).Select(i => $"Strength {i + 1}")], [], "One pass"),
        };

    private Guid Seed(
        int draftNumber, int words, Guid? projectId = null,
        BookAnalysisResult? report = null, string? pdf = null,
        Guid? seriesId = null, int sequence = 1)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "The Salt Line",
            GoogleDocId = "doc",
            CreatedAtUtc = clock.UtcNow.AddDays(draftNumber),
            UpdatedAtUtc = clock.UtcNow.AddDays(draftNumber),
            DraftNumber = draftNumber,
            WordCount = words,
            Stage = ProjectStage.Completed,
            SeriesId = seriesId,
            SeriesSequence = sequence,
        };
        novel.ProjectId = projectId ?? novel.Id;
        if (report is not null)
        {
            novel.Analysis = new BookAnalysis
            {
                NovelId = novel.Id,
                ConsolidatedJson = JsonSerializer.Serialize(report),
                PdfStatus = pdf is null ? WorkStatus.Pending : WorkStatus.Completed,
                PdfPath = pdf,
            };
        }
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }
}
