using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Model;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.Imaging;
using Storyboarder.Infrastructure.Services;
using Storyboarder.Tests.Support;
using Xunit;

namespace Storyboarder.Tests.Services;

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
        Assert.Equal("My Book", second.Title);
        Assert.Equal("noir", second.ArtStyle);
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
    public async Task GetDraftDiff_ReportsResolvedAndNewFindings()
    {
        var previousReport = BookAnalysisResult.Empty with
        {
            PlotHoles = [new ReportFinding("Chapter 1", "The gate opens unexplained")],
            Inconsistencies = [new ReportFinding("Chapter 2", "Eye colour shifts")]
        };
        var currentReport = BookAnalysisResult.Empty with
        {
            // The gate hole is resolved; the eye-colour issue persists; a new pacing note appears.
            Inconsistencies = [new ReportFinding("Chapter 2", "Eye colour shifts")],
            PacingNotes = [new ReportFinding("Chapter 5", "The middle sags")]
        };

        var previousId = SeedReport(previousReport);
        var currentId = SeedReport(currentReport, previousDraftId: previousId, projectId: previousId);

        var diff = await NewService().GetDraftDiffAsync(currentId, CancellationToken.None);

        Assert.Equal(currentId, diff.DraftId);
        Assert.Equal(previousId, diff.PreviousDraftId);
        Assert.Contains(diff.ResolvedFindings, f => f.Detail == "The gate opens unexplained");
        Assert.Contains(diff.NewFindings, f => f.Detail == "The middle sags");
        Assert.DoesNotContain(diff.ResolvedFindings, f => f.Detail == "Eye colour shifts");
        Assert.DoesNotContain(diff.NewFindings, f => f.Detail == "Eye colour shifts");
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
    public async Task VerifyAndHeal_LeavesAHealthyFinishedReportUntouched()
    {
        var path = imageStore.Save("book/analysis/book-analysis.pdf", [1, 2, 3]);
        var novelId = SeedReportOnDisk(ProjectStage.Completed, path);

        var healed = await NewService().VerifyAndHealOutputsAsync(CancellationToken.None);

        Assert.Equal(0, healed);
        using var context = database.NewContext();
        Assert.Equal(ProjectStage.Completed, context.Novels.Single(n => n.Id == novelId).Stage);
    }

    [Fact]
    public async Task VerifyAndHeal_ReRendersAMissingReport_AndRegressesTheStage()
    {
        var path = imageStore.Save("book/analysis/book-analysis.pdf", [1, 2, 3]);
        var novelId = SeedReportOnDisk(ProjectStage.Completed, path);
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
            var novel = new Novel { Title = "Quest", GoogleDocId = "doc", Stage = ProjectStage.Completed, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
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
}
