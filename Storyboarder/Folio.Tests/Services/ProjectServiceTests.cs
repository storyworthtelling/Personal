using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq;
using Folio.Domain.Analysis;
using Folio.Domain.External;
using Folio.Domain.Model;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Imaging;
using Folio.Infrastructure.Services;
using Folio.Tests.Support;
using Xunit;

namespace Folio.Tests.Services;

public class ProjectServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly Mock<IGoogleDocsClient> docs = new();
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "canonizer-projectservice", Guid.NewGuid().ToString("N"));
    private readonly FileSystemImageStore imageStore;

    public ProjectServiceTests()
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

    private Guid SeedNovel(ProjectStage stage)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Quest", GoogleDocId = "doc", Stage = stage,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    [Fact]
    public async Task HasActiveWork_IsFalse_WhenNoManuscripts()
    {
        Assert.False(await NewService().HasActiveWorkAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(ProjectStage.Ingesting)]
    [InlineData(ProjectStage.BuildingDesignBible)]
    [InlineData(ProjectStage.RenderingSceneImages)]
    [InlineData(ProjectStage.ComposingAnalysis)]
    public async Task HasActiveWork_IsTrue_ForNonTerminalManuscript(ProjectStage stage)
    {
        SeedNovel(stage);
        Assert.True(await NewService().HasActiveWorkAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(ProjectStage.Completed)]
    [InlineData(ProjectStage.Failed)]
    [InlineData(ProjectStage.Cancelled)]
    public async Task HasActiveWork_IsFalse_WhenAllManuscriptsTerminal(ProjectStage stage)
    {
        SeedNovel(stage);
        Assert.False(await NewService().HasActiveWorkAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CreateFromDoc_StartsANewOneDraftProject()
    {
        docs.Setup(d => d.FetchText("doc-1", "token", It.IsAny<CancellationToken>()))
            .ReturnsAsync("A short tale.");

        var id = await NewService().CreateFromDocAsync("doc-1", "Book.gdoc", "token", null, null, CancellationToken.None);

        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == id);
        Assert.Equal(id, novel.ProjectId);
        Assert.Equal(1, novel.DraftNumber);
        Assert.Null(novel.PreviousDraftId);
    }

    [Fact]
    public async Task CreateNextDraft_AddsDraftTwoToTheSameProject()
    {
        docs.Setup(d => d.FetchText("doc-1", "token", It.IsAny<CancellationToken>()))
            .ReturnsAsync("First draft.");
        docs.Setup(d => d.FetchText("doc-2", "token", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Second, revised draft.");

        var service = NewService();
        var firstId = await service.CreateFromDocAsync("doc-1", "Book.gdoc", "token", "My Book", "noir", CancellationToken.None);
        var secondId = await service.CreateNextDraftAsync(firstId, "doc-2", "Book-v2.gdoc", "token", null, null, CancellationToken.None);

        Assert.NotNull(secondId);
        using var context = database.NewContext();
        var first = context.Novels.Single(n => n.Id == firstId);
        var second = context.Novels.Single(n => n.Id == secondId!.Value);

        Assert.Equal(first.ProjectId, second.ProjectId);
        Assert.Equal(2, second.DraftNumber);
        Assert.Equal(firstId, second.PreviousDraftId);
        Assert.Equal("Second, revised draft.", context.SourceTexts.Single(s => s.NovelId == second.Id).FullText);
        // The draft is titled from its own picked manuscript (extension stripped), not the prior draft's title.
        Assert.Equal("Book-v2", second.Title);
        Assert.Equal("noir", second.ArtStyle);
    }

    [Fact]
    public async Task CreateNextDraft_TitlesFromItsOwnManuscript_NotThePreviousDraft()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");

        var service = NewService();
        var firstId = await service.CreateFromDocAsync("doc-1", "The First Cut.gdoc", "token", null, null, CancellationToken.None);
        // Null title (what the UI always passes) must fall back to the newly picked file's name, not "The First Cut".
        var secondId = await service.CreateNextDraftAsync(firstId, "doc-2", "The Second Pass.docx", "token", null, null, CancellationToken.None);

        using var context = database.NewContext();
        Assert.Equal("The Second Pass", context.Novels.Single(n => n.Id == secondId!.Value).Title);
    }

    [Fact]
    public async Task CreateNextBook_TitlesFromItsOwnManuscript_NotThePriorBook()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");

        var service = NewService();
        var bookOne = await service.CreateFromDocAsync("doc-1", "Gathering Storm.gdoc", "token", null, null, CancellationToken.None);
        // The real UI passes null here — the sequel must be titled from its own manuscript, not book one's.
        var bookTwo = await service.CreateNextBookAsync(bookOne, "doc-2", "Breaking Dawn.docx", "token", null, null, CancellationToken.None);

        using var context = database.NewContext();
        var one = context.Novels.Single(n => n.Id == bookOne);
        var two = context.Novels.Single(n => n.Id == bookTwo!.Value);
        Assert.Equal("Gathering Storm", one.Title);
        Assert.Equal("Breaking Dawn", two.Title);
    }

    [Fact]
    public async Task CreateNextDraft_NumbersFromTheLatestDraft_NotTheOneBranchedFrom()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");

        var service = NewService();
        var d1 = await service.CreateFromDocAsync("doc-1", "Book.gdoc", "token", null, null, CancellationToken.None);
        var d2 = await service.CreateNextDraftAsync(d1, "doc-2", "Book.gdoc", "token", null, null, CancellationToken.None);
        var d3 = await service.CreateNextDraftAsync(d1, "doc-3", "Book.gdoc", "token", null, null, CancellationToken.None);

        using var context = database.NewContext();
        Assert.Equal(3, context.Novels.Single(n => n.Id == d3!.Value).DraftNumber);
        Assert.Equal(2, context.Novels.Single(n => n.Id == d2!.Value).DraftNumber);
    }

    [Fact]
    public async Task CreateNextDraft_ReturnsNull_WhenThePreviousDraftIsMissing()
    {
        var result = await NewService().CreateNextDraftAsync(
            Guid.NewGuid(), "doc", "Book.gdoc", "token", null, null, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateNextBook_StartsANewProject_LinkedAsBookTwoOfASeries()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");

        var service = NewService();
        var bookOne = await service.CreateFromDocAsync("doc-1", "Book One.gdoc", "token", "Book One", "noir", CancellationToken.None);
        var bookTwo = await service.CreateNextBookAsync(bookOne, "doc-2", "Book Two.gdoc", "token", "Book Two", null, CancellationToken.None);

        Assert.NotNull(bookTwo);
        using var context = database.NewContext();
        var one = context.Novels.Single(n => n.Id == bookOne);
        var two = context.Novels.Single(n => n.Id == bookTwo!.Value);

        // A new book is its own project (a fresh draft chain), not another draft of book one.
        Assert.Equal(two.Id, two.ProjectId);
        Assert.NotEqual(one.ProjectId, two.ProjectId);
        Assert.Equal(1, two.DraftNumber);
        Assert.Null(two.PreviousDraftId);

        // But it is linked into the series book one now heads.
        Assert.NotNull(two.SeriesId);
        Assert.Equal(one.SeriesId, two.SeriesId);
        Assert.Equal(bookOne, two.PreviousBookId);   // book one's only (and latest) draft is the canon source
        Assert.Equal(2, two.SeriesSequence);
        Assert.Equal("Book Two", two.Title);
        Assert.Equal("noir", two.ArtStyle);           // inherited from book one when not overridden
    }

    [Fact]
    public async Task CreateNextBook_BackfillsTheSeriesOntoBookOnesDrafts()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");

        var service = NewService();
        var d1 = await service.CreateFromDocAsync("doc-1", "Book.gdoc", "token", null, null, CancellationToken.None);
        var d2 = await service.CreateNextDraftAsync(d1, "doc-1b", "Book.gdoc", "token", null, null, CancellationToken.None);
        var bookTwo = await service.CreateNextBookAsync(d2!.Value, "doc-2", "Book Two.gdoc", "token", null, null, CancellationToken.None);

        using var context = database.NewContext();
        var first = context.Novels.Single(n => n.Id == d1);
        var second = context.Novels.Single(n => n.Id == d2.Value);
        var seriesId = context.Novels.Single(n => n.Id == bookTwo!.Value).SeriesId;

        // Book one had no series; both its drafts are backfilled with the new series id at sequence 1.
        Assert.Equal(seriesId, first.SeriesId);
        Assert.Equal(seriesId, second.SeriesId);
        Assert.Equal(1, first.SeriesSequence);
        Assert.Equal(1, second.SeriesSequence);
        Assert.Equal(first.ProjectId, seriesId);      // the series id is book one's project id
    }

    [Fact]
    public async Task CreateNextBook_PointsAtThePriorBooksLatestCompletedDraft()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");

        var service = NewService();
        var d1 = await service.CreateFromDocAsync("doc-1", "Book.gdoc", "token", null, null, CancellationToken.None);
        var d2 = await service.CreateNextDraftAsync(d1, "doc-1b", "Book.gdoc", "token", null, null, CancellationToken.None);
        // Only draft 2 has finished; draft 1 is still mid-review.
        using (var seed = database.NewContext())
        {
            seed.Novels.Single(n => n.Id == d2!.Value).Stage = ProjectStage.Completed;
            seed.SaveChanges();
        }

        // The owner taps book one's (draft-1) card to start the sequel; canon comes from its latest COMPLETED draft.
        var bookTwo = await service.CreateNextBookAsync(d1, "doc-2", "Book Two.gdoc", "token", null, null, CancellationToken.None);

        using var context = database.NewContext();
        Assert.Equal(d2!.Value, context.Novels.Single(n => n.Id == bookTwo!.Value).PreviousBookId);
    }

    [Fact]
    public async Task CreateNextBook_ReturnsNull_WhenThePriorBookIsMissing()
    {
        var result = await NewService().CreateNextBookAsync(
            Guid.NewGuid(), "doc", "Book.gdoc", "token", null, null, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateNextDraft_CarriesTheSeriesLinkForward()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");

        var service = NewService();
        var bookOne = await service.CreateFromDocAsync("doc-1", "Book One.gdoc", "token", null, null, CancellationToken.None);
        var bookTwoD1 = await service.CreateNextBookAsync(bookOne, "doc-2", "Book Two.gdoc", "token", null, null, CancellationToken.None);
        // A second draft of book two must stay linked to book one.
        var bookTwoD2 = await service.CreateNextDraftAsync(bookTwoD1!.Value, "doc-2b", "Book Two.gdoc", "token", null, null, CancellationToken.None);

        using var context = database.NewContext();
        var d1 = context.Novels.Single(n => n.Id == bookTwoD1.Value);
        var d2 = context.Novels.Single(n => n.Id == bookTwoD2!.Value);

        Assert.Equal(d1.SeriesId, d2.SeriesId);
        Assert.Equal(d1.PreviousBookId, d2.PreviousBookId);
        Assert.Equal(d1.SeriesSequence, d2.SeriesSequence);
        Assert.Equal(2, d2.SeriesSequence);   // still book two
    }

    [Fact]
    public async Task GetReport_FoldsStoryHalfWithCraft()
    {
        var story = BookAnalysisResult.Empty with
        {
            Logline = "A hero's tale",
            PlotHoles = [new ReportFinding("Chapter 1", "The gate opens unexplained")]
        };
        var craft = new CraftReport([new LineEdit("Chapter 1", "He felt cold.", "The cold came.", "Filtered.")], [], [], []);

        var novelId = SeedReport(story, craft);

        var report = await NewService().GetReportAsync(novelId, CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal("A hero's tale", report!.Logline);
        Assert.Single(report.PlotHoles);
        Assert.Single(report.LineEdits);   // craft half folded in
    }

    [Fact]
    public async Task GetReport_IsNull_BeforeTheReportIsConsolidated()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");
        var id = await NewService().CreateFromDocAsync("doc", "Book.gdoc", "token", null, null, CancellationToken.None);

        Assert.Null(await NewService().GetReportAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task GetDraftDiff_ReportsResolvedNewAndRegressed_ByThreadIdentity()
    {
        // Findings are matched across drafts by their stable ThreadId, not by text.
        var gateThread = Guid.NewGuid();      // resolved this draft
        var eyeThread = Guid.NewGuid();       // carried forward
        var dateThread = Guid.NewGuid();      // fixed earlier, now regressed

        var previousId = SeedDraftWithFindings(previousDraftId: null, draftNumber: 1,
        [
            (gateThread, FindingCategory.PlotHole, "Chapter 1", "The gate opens unexplained", FindingStatus.New),
            (eyeThread, FindingCategory.Inconsistency, "Chapter 2", "Eye colour shifts", FindingStatus.Carried)
        ]);
        var currentId = SeedDraftWithFindings(previousDraftId: previousId, draftNumber: 2,
        [
            // The gate thread is gone (resolved); the eye thread carries; a new pacing thread; a regressed date.
            (eyeThread, FindingCategory.Inconsistency, "Chapter 2", "Eye colour shifts, now green", FindingStatus.Carried),
            (Guid.NewGuid(), FindingCategory.Pacing, "Chapter 5", "The middle sags", FindingStatus.New),
            (dateThread, FindingCategory.Inaccuracy, "Chapter 3", "Wrong coronation date", FindingStatus.Regressed)
        ]);

        var diff = await NewService().GetDraftDiffAsync(currentId, CancellationToken.None);

        Assert.Equal(currentId, diff.DraftId);
        Assert.Equal(previousId, diff.PreviousDraftId);
        Assert.Contains(diff.ResolvedFindings, f => f.Detail == "The gate opens unexplained");
        Assert.Contains(diff.NewFindings, f => f.Detail == "The middle sags");
        Assert.Contains(diff.RegressedFindings, f => f.Detail == "Wrong coronation date");
        // The carried thread appears in none of the change lists — it's still open, unchanged in status.
        Assert.DoesNotContain(diff.ResolvedFindings, f => f.Detail.StartsWith("Eye colour"));
        Assert.DoesNotContain(diff.NewFindings, f => f.Detail.StartsWith("Eye colour"));
    }

    [Fact]
    public async Task GetDraftDiff_IncludesBeforeAfterScenePairs_ForChangedScenes()
    {
        var prevSceneId = Guid.NewGuid();
        var prevImage = imageStore.Save("prev/scene-1.png", [1, 2, 3]);
        var previousId = SeedDraftWithScene(previousDraftId: null, sceneId: prevSceneId,
            number: 1, changed: false, previousSceneId: null, imagePath: prevImage);

        var newImage = imageStore.Save("cur/scene-1.png", [4, 5, 6]);
        var currentId = SeedDraftWithScene(previousDraftId: previousId, sceneId: Guid.NewGuid(),
            number: 1, changed: true, previousSceneId: prevSceneId, imagePath: newImage);

        var diff = await NewService().GetDraftDiffAsync(currentId, CancellationToken.None);

        var pair = Assert.Single(diff.ChangedScenes);
        Assert.Equal(new byte[] { 1, 2, 3 }, pair.PreviousImage);   // the previous draft's render
        Assert.Equal(new byte[] { 4, 5, 6 }, pair.NewImage);        // this draft's fresh render
    }

    [Fact]
    public async Task GetDraftDiff_OfAFirstDraft_IsEmpty()
    {
        var id = SeedReport(BookAnalysisResult.Empty with { PlotHoles = [new ReportFinding("Chapter 1", "A hole")] });

        var diff = await NewService().GetDraftDiffAsync(id, CancellationToken.None);

        Assert.Null(diff.PreviousDraftId);
        Assert.Empty(diff.ResolvedFindings);
        Assert.Empty(diff.NewFindings);
    }

    [Fact]
    public async Task GetStatus_SurfacesDraftNumberAndWhetherThereIsAPreviousDraft()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");
        var service = NewService();
        var d1 = await service.CreateFromDocAsync("doc-1", "Book.gdoc", "token", null, null, CancellationToken.None);
        var d2 = await service.CreateNextDraftAsync(d1, "doc-2", "Book.gdoc", "token", null, null, CancellationToken.None);

        var status = await service.GetStatusAsync(d2!.Value, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(2, status!.DraftNumber);
        Assert.True(status.HasPreviousDraft);
    }

    [Fact]
    public async Task GetStatus_SurfacesTheSeriesBadge_ForALinkedBook()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");
        var service = NewService();
        var bookOne = await service.CreateFromDocAsync("doc-1", "Book One.gdoc", "token", null, null, CancellationToken.None);
        var bookTwo = await service.CreateNextBookAsync(bookOne, "doc-2", "Book Two.gdoc", "token", null, null, CancellationToken.None);

        var twoStatus = await service.GetStatusAsync(bookTwo!.Value, CancellationToken.None);
        var oneStatus = await service.GetStatusAsync(bookOne, CancellationToken.None);

        Assert.NotNull(twoStatus);
        Assert.True(twoStatus!.InSeries);
        Assert.Equal(2, twoStatus.SeriesSequence);
        // Book one was backfilled into the series it now heads.
        Assert.True(oneStatus!.InSeries);
        Assert.Equal(1, oneStatus.SeriesSequence);
    }

    [Fact]
    public async Task GetStatus_HasNoSeriesBadge_ForAStandaloneBook()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");
        var service = NewService();
        var loneBook = await service.CreateFromDocAsync("doc-1", "Book.gdoc", "token", null, null, CancellationToken.None);

        var status = await service.GetStatusAsync(loneBook, CancellationToken.None);

        Assert.NotNull(status);
        Assert.False(status!.InSeries);
    }

    // Staged at Archiving, not Completed: a Completed book is excluded from the heal selection
    // outright (see CompletedBooksDoNotHealTests), so seeding one here would pass whether the
    // file-exists check worked or not — and that check is what this test is for.
    [Fact]
    public async Task VerifyAndHeal_LeavesAHealthyReportUntouched()
    {
        var path = imageStore.Save("book/analysis/book-analysis.pdf", [1, 2, 3]);
        var novelId = SeedReportOnDisk(ProjectStage.Archiving, path);

        var healed = await NewService().VerifyAndHealOutputsAsync(CancellationToken.None);

        Assert.Equal(0, healed);
        using var context = database.NewContext();
        Assert.Equal(ProjectStage.Archiving, context.Novels.Single(n => n.Id == novelId).Stage);
    }

    [Fact]
    public async Task VerifyAndHeal_ReRendersAMissingReport_AndRegressesTheStage()
    {
        var path = imageStore.Save("book/analysis/book-analysis.pdf", [1, 2, 3]);
        var novelId = SeedReportOnDisk(ProjectStage.Archiving, path);
        File.Delete(path); // a reboot or storage hiccup lost the rendered report

        var healed = await NewService().VerifyAndHealOutputsAsync(CancellationToken.None);

        Assert.Equal(1, healed);
        using var context = database.NewContext();
        Assert.Equal(WorkStatus.Pending, context.BookAnalyses.Single(a => a.NovelId == novelId).PdfStatus);
        Assert.Equal(ProjectStage.ComposingAnalysis, context.Novels.Single(n => n.Id == novelId).Stage);
    }

    [Fact]
    public async Task VerifyAndHeal_ReRendersASceneImageWhoseFileIsGone()
    {
        var scenePath = imageStore.Save("book/scenes/scene-1.png", [4, 5, 6]);
        Guid novelId;
        using (var context = database.NewContext())
        {
            var novel = new Novel { Title = "Quest", GoogleDocId = "doc", Stage = ProjectStage.ComposingAnalysis, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
            novel.ProjectId = novel.Id;
            novel.Scenes.Add(new Scene { NovelId = novel.Id, Number = 1, ImageStatus = WorkStatus.Completed, ImagePath = scenePath });
            context.Novels.Add(novel);
            context.SaveChanges();
            novelId = novel.Id;
        }
        File.Delete(scenePath);

        var healed = await NewService().VerifyAndHealOutputsAsync(CancellationToken.None);

        Assert.Equal(1, healed);
        using var check = database.NewContext();
        Assert.Equal(WorkStatus.Pending, check.Scenes.Single(s => s.NovelId == novelId).ImageStatus);
        Assert.Equal(ProjectStage.RenderingSceneImages, check.Novels.Single(n => n.Id == novelId).Stage);
    }

    private Guid SeedReport(BookAnalysisResult story, CraftReport? craft = null, Guid? previousDraftId = null, Guid? projectId = null)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Quest", GoogleDocId = "doc", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow,
            PreviousDraftId = previousDraftId
        };
        novel.ProjectId = projectId ?? novel.Id;
        if (previousDraftId is not null)
            novel.DraftNumber = 2;
        novel.Analysis = new BookAnalysis
        {
            NovelId = novel.Id,
            ConsolidatedJson = JsonSerializer.Serialize(story),
            CraftJson = craft is null ? null : JsonSerializer.Serialize(craft),
            PdfStatus = WorkStatus.Completed
        };
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    // Seeds a draft carrying persisted Finding rows (the identity model the diff now reads), so a diff test
    // can set up thread identities and statuses directly. ProjectId follows the previous draft when given.
    private Guid SeedDraftWithFindings(
        Guid? previousDraftId, int draftNumber,
        IReadOnlyList<(Guid ThreadId, FindingCategory Category, string Chapter, string Detail, FindingStatus Status)> findings)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Quest", GoogleDocId = "doc", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow,
            PreviousDraftId = previousDraftId, DraftNumber = draftNumber
        };
        novel.ProjectId = previousDraftId ?? novel.Id;
        foreach (var f in findings)
            novel.Findings.Add(new Finding
            {
                NovelId = novel.Id, ProjectId = novel.ProjectId, ThreadId = f.ThreadId,
                Category = f.Category, Chapter = f.Chapter, Detail = f.Detail, Status = f.Status,
                FirstDraftNumber = 1, CreatedAtUtc = clock.UtcNow
            });
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    // Seeds a draft carrying one scene with its cross-draft linkage + image path, for the scene-pair diff.
    private Guid SeedDraftWithScene(
        Guid? previousDraftId, Guid sceneId, int number, bool changed, Guid? previousSceneId, string imagePath)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Quest", GoogleDocId = "doc", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow,
            PreviousDraftId = previousDraftId, DraftNumber = previousDraftId is null ? 1 : 2
        };
        novel.ProjectId = previousDraftId ?? novel.Id;
        novel.Scenes.Add(new Scene
        {
            Id = sceneId, NovelId = novel.Id, Number = number, ChapterNumber = 1, ChapterLabel = "Chapter 1",
            Changed = changed, PreviousSceneId = previousSceneId,
            ImageStatus = WorkStatus.Completed, ImagePath = imagePath
        });
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    private Guid SeedReportOnDisk(ProjectStage stage, string pdfPath)
    {
        using var context = database.NewContext();
        var novel = new Novel { Title = "Quest", GoogleDocId = "doc", Stage = stage, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        novel.ProjectId = novel.Id;
        novel.Analysis = new BookAnalysis { NovelId = novel.Id, PdfStatus = WorkStatus.Completed, PdfPath = pdfPath };
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    // A manuscript parked after its attempts ran out (or waiting out a backoff) must be runnable again the
    // moment the owner taps Retry — a stale gate would leave the button doing nothing.
    [Fact]
    public async Task Retry_GivesAParkedManuscriptItsAttemptsBackAndClearsTheWait()
    {
        var id = SeedNovel(ProjectStage.ComposingAnalysis);
        using (var context = database.NewContext())
        {
            var novel = context.Novels.Single(n => n.Id == id);
            novel.StageAttempts = 6;
            novel.LastError = "Reading pass 'x': HttpRequestException: connection abort";
            novel.RetryNotBeforeUtc = clock.UtcNow.AddHours(1);
            context.SaveChanges();
        }

        await NewService().RetryAsync(id, CancellationToken.None);

        using var after = database.NewContext();
        var retried = after.Novels.Single(n => n.Id == id);
        Assert.Equal(0, retried.StageAttempts);
        Assert.Null(retried.LastError);
        Assert.Null(retried.RetryNotBeforeUtc);
    }
}
