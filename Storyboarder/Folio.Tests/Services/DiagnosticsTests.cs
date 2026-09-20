using Microsoft.Extensions.Options;
using Moq;
using Folio.Domain.External;
using Folio.Domain.Model;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Imaging;
using Folio.Infrastructure.Services;
using Folio.Tests.Support;
using Xunit;

namespace Folio.Tests.Services;

/// <summary>
/// The diagnostics export, which had no test and — more to the point — said nothing about the
/// document.
///
/// <para>It reported every stage of the pipeline and every stuck unit, which is what to read when a
/// review will not finish. It was silent on the one question an owner actually asks once a review
/// HAS finished: where is the report, and did it go anywhere? A finished book whose delivery quietly
/// gave up looks exactly like a finished book whose delivery worked — the stage is Completed either
/// way, and the give-up is a log line on a phone nobody can read.</para>
/// </summary>
public class DiagnosticsTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 9, 20, 3, 0, 0, TimeSpan.Zero));
    private readonly Mock<IGoogleDocsClient> docs = new();
    private readonly string outputRoot = Path.Combine(
        Path.GetTempPath(), "folio-diagnostics", Guid.NewGuid().ToString("N"));
    private readonly FileSystemImageStore imageStore;

    public DiagnosticsTests()
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
    public async Task ABookWithNoReportSaysSo()
    {
        Seed(analysis: null);

        Assert.Contains("Report: not rendered", await Report(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARenderedButUndeliveredReportSaysWhereItIs()
    {
        Seed(new BookAnalysis { PdfStatus = WorkStatus.Completed, PdfPath = "book/analysis/report.pdf" });

        var report = await Report();

        Assert.Contains("Report: rendered", report, StringComparison.Ordinal);
        Assert.Contains("on this device only", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeliveredReportNamesBothTargets()
    {
        Seed(new BookAnalysis
        {
            PdfStatus = WorkStatus.Completed,
            PdfPath = "book/analysis/report.pdf",
            ArchivedDriveFileId = "drive-file-id",
            DeliveredToRemarkable = true,
        });

        var report = await Report();

        Assert.Contains("Drive", report, StringComparison.Ordinal);
        Assert.Contains("reMarkable", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// A render that failed is the one state the stage cannot show. The book still reaches
    /// Completed — deliberately, so a broken renderer cannot wedge a review — so without this line
    /// the export says a finished book and means a book with no document.
    /// </summary>
    [Fact]
    public async Task AFailedRenderIsNamed()
    {
        Seed(new BookAnalysis { PdfStatus = WorkStatus.Failed });

        Assert.Contains("Report: could not be rendered", await Report(), StringComparison.Ordinal);
    }

    /// <summary>The name the document was delivered under, so the owner can search Drive for it.</summary>
    [Fact]
    public async Task TheExportNamesTheFileTheOwnerIsLookingFor()
    {
        Seed(new BookAnalysis
        {
            PdfStatus = WorkStatus.Completed,
            PdfPath = "book/analysis/report.pdf",
            ArchivedDriveFileId = "drive-file-id",
        });

        Assert.Contains(
            "The Salt Line — Draft 3 — Revision report.pdf", await Report(), StringComparison.Ordinal);
    }

    private Task<string> Report() =>
        new ProjectService(database, docs.Object, imageStore, clock, Options.Create(new RateLimitOptions()))
            .BuildDiagnosticsReportAsync(
                new DiagnosticsContext(true, true, "owner@example.com", "ok", clock.UtcNow, null),
                CancellationToken.None);

    private void Seed(BookAnalysis? analysis)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "The Salt Line",
            GoogleDocId = "doc",
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow,
            DraftNumber = 3,
            Stage = ProjectStage.Completed,
        };
        novel.ProjectId = novel.Id;
        if (analysis is not null)
        {
            analysis.NovelId = novel.Id;
            novel.Analysis = analysis;
        }
        context.Novels.Add(novel);
        context.SaveChanges();
    }
}
