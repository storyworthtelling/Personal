using Microsoft.Extensions.Options;
using Moq;
using Folio.Domain.External;
using Folio.Domain.Model;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Imaging;
using Folio.Infrastructure.Services;
using Folio.Tests.Support;
using Xunit;

namespace Folio.Tests.Guard;

/// <summary>
/// G2 — a finished book is never pulled back into the pipeline (R-1).
///
/// <para><b>The heal path is right; its selection was wrong.</b> <c>VerifyAndHealOutputsAsync</c>
/// runs at every app start and repairs a unit whose rendered file has gone missing — a reboot
/// mid-flush, a storage sweep, an interrupted write. For an <i>unfinished</i> book that is exactly
/// the behaviour wanted. But it selected every novel that was not Cancelled or Failed, which
/// included <c>Completed</c>, and it writes <c>novel.Stage</c> backwards <b>directly</b> rather than
/// through <c>ProjectStateMachine.NextStage</c> — so the state machine having <c>Completed</c> as a
/// terminal stage did not protect anything.</para>
///
/// <para>What that costs is the reason this is its own release. A whole-manuscript re-read is the
/// most expensive thing this app does, and a finished book restarting is not a nuisance — it is the
/// same bill a second time, arriving because a file the owner never touched went missing.</para>
///
/// <para><b>The accepted cost, stated:</b> a Completed book whose report PDF vanishes stays Completed
/// with no PDF on disk, rather than quietly rebuilding. It is a smaller cost than it first looks:
/// the report itself is not in the PDF, it is in <c>BookAnalysis</c>'s stored JSON, so
/// <c>GetReportAsync</c> still shows the whole thing in the app, and an archived book still has its
/// Drive copy. One local file is lost, and regaining it is the owner's deliberate act.</para>
/// </summary>
public class CompletedBooksDoNotHealTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly Mock<IGoogleDocsClient> docs = new();
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "folio-heal-guard", Guid.NewGuid().ToString("N"));
    private readonly FileSystemImageStore imageStore;

    public CompletedBooksDoNotHealTests()
    {
        imageStore = new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot }));
    }

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }

    private ProjectService NewService() =>
        new(database, docs.Object, imageStore, clock, Options.Create(new RateLimitOptions()));

    [Fact]
    public async Task AFinishedBookWithAMissingReportIsLeftAlone()
    {
        var pdfPath = imageStore.Save("book/analysis/book-analysis.pdf", [1, 2, 3]);
        var novelId = SeedFinishedBook(pdfPath, scenePath: null);
        File.Delete(pdfPath);

        var healed = await NewService().VerifyAndHealOutputsAsync(CancellationToken.None);

        Assert.Equal(0, healed);
        using var context = database.NewContext();
        Assert.Equal(ProjectStage.Completed, context.Novels.Single(n => n.Id == novelId).Stage);
        // Still Completed on the record too: a re-run would re-bill the compose stage.
        Assert.Equal(WorkStatus.Completed, context.BookAnalyses.Single(a => a.NovelId == novelId).PdfStatus);
    }

    [Fact]
    public async Task AFinishedBookWithAMissingSceneImageIsLeftAlone()
    {
        var scenePath = imageStore.Save("book/scenes/scene-1.png", [4, 5, 6]);
        var novelId = SeedFinishedBook(pdfPath: null, scenePath);
        File.Delete(scenePath);

        var healed = await NewService().VerifyAndHealOutputsAsync(CancellationToken.None);

        Assert.Equal(0, healed);
        using var context = database.NewContext();
        Assert.Equal(ProjectStage.Completed, context.Novels.Single(n => n.Id == novelId).Stage);
        Assert.Equal(WorkStatus.Completed, context.Scenes.Single(s => s.NovelId == novelId).ImageStatus);
    }

    // The other half of the guard: excluding Completed must not turn the heal path off. An
    // unfinished book with the same missing file is still repaired, which is what it is for.
    [Fact]
    public async Task AnUnfinishedBookWithTheSameMissingFileIsStillHealed()
    {
        var scenePath = imageStore.Save("book/scenes/scene-1.png", [4, 5, 6]);
        var novelId = SeedFinishedBook(pdfPath: null, scenePath, ProjectStage.ComposingAnalysis);
        File.Delete(scenePath);

        var healed = await NewService().VerifyAndHealOutputsAsync(CancellationToken.None);

        Assert.Equal(1, healed);
        using var context = database.NewContext();
        Assert.Equal(ProjectStage.RenderingSceneImages, context.Novels.Single(n => n.Id == novelId).Stage);
    }

    private Guid SeedFinishedBook(string? pdfPath, string? scenePath, ProjectStage stage = ProjectStage.Completed)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Quest",
            GoogleDocId = "doc",
            Stage = stage,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow,
        };
        novel.ProjectId = novel.Id;

        if (pdfPath is not null)
            novel.Analysis = new BookAnalysis { NovelId = novel.Id, PdfStatus = WorkStatus.Completed, PdfPath = pdfPath };
        if (scenePath is not null)
            novel.Scenes.Add(new Scene { NovelId = novel.Id, Number = 1, ImageStatus = WorkStatus.Completed, ImagePath = scenePath });

        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }
}
