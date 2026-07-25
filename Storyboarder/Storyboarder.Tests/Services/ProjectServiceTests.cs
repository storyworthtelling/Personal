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
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "storyboarder-projectservice", Guid.NewGuid().ToString("N"));
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
            Title = "Quest",
            GoogleDocId = "doc",
            Stage = stage,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    [Fact]
    public async Task HasActiveWork_IsFalse_WhenNoNovels()
    {
        Assert.False(await NewService().HasActiveWorkAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(ProjectStage.Created)]
    [InlineData(ProjectStage.BuildingDesignBible)]
    [InlineData(ProjectStage.RenderingImages)]
    [InlineData(ProjectStage.ComposingAnalysis)]
    public async Task HasActiveWork_IsTrue_ForNonTerminalNovel(ProjectStage stage)
    {
        SeedNovel(stage);
        Assert.True(await NewService().HasActiveWorkAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(ProjectStage.Completed)]
    [InlineData(ProjectStage.Failed)]
    [InlineData(ProjectStage.Cancelled)]
    public async Task HasActiveWork_IsFalse_WhenAllNovelsTerminal(ProjectStage stage)
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
        // A lone novel is a project of one draft: its ProjectId is its own id, numbered 1, with nothing
        // before it. This is what a later draft groups against.
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

        // Same project, next number, chained to its predecessor — the link the diff and incremental
        // re-render both read.
        Assert.Equal(first.ProjectId, second.ProjectId);
        Assert.Equal(2, second.DraftNumber);
        Assert.Equal(firstId, second.PreviousDraftId);
        // The revised manuscript is the new draft's source, and title/style carry forward unless overridden.
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
        // Branch a third draft off draft ONE, not the latest: it must still be numbered 3.
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
    public async Task GetDraftDiff_ReportsResolvedAndNewFindings_AndTheChangedPanels()
    {
        // Two drafts of one book. The middle scene's text was rewritten between them; the outer scenes
        // are untouched. The report resolved one finding and raised a new one.
        const string firstText = "AAAAA BBBBB CCCCC";
        const string secondText = "AAAAA XXXXX CCCCC";
        var projectId = Guid.NewGuid();

        var firstReport = BookAnalysisResult.Empty with
        {
            PlotHoles = [new Finding("Chapter 1", "The gate opens unexplained")],
            Inconsistencies = [new Finding("Chapter 2", "The hero's eye colour shifts")]
        };
        var secondReport = BookAnalysisResult.Empty with
        {
            PlotHoles = [new Finding("Chapter 1", "The gate opens unexplained")],
            Inaccuracies = [new Finding("Chapter 3", "Paris is placed in Germany")]
        };

        var firstId = SeedDraft(projectId, 1, null, firstText, firstReport,
            (1, 0, 5, "first-1.png"), (2, 6, 11, "first-2.png"), (3, 12, 17, "first-3.png"));
        var secondId = SeedDraft(projectId, 2, firstId, secondText, secondReport,
            (1, 0, 5, "second-1.png"), (2, 6, 11, "second-2.png"), (3, 12, 17, "second-3.png"));

        var diff = await NewService().GetDraftDiffAsync(secondId, CancellationToken.None);

        Assert.Equal(secondId, diff.DraftId);
        Assert.Equal(firstId, diff.PreviousDraftId);

        // The eye-colour inconsistency is gone; the Paris inaccuracy is new. The unchanged plot hole
        // shows in neither list.
        var resolved = Assert.Single(diff.ResolvedFindings);
        Assert.Equal("Inconsistencies", resolved.Category);
        Assert.Contains("eye colour", resolved.Detail);
        var added = Assert.Single(diff.NewFindings);
        Assert.Equal("Inaccuracies", added.Category);
        Assert.Contains("Paris", added.Detail);

        // Only the middle panel sits on rewritten text, so only it re-renders; the other two carry forward.
        Assert.Equal(2, diff.ReusablePanelCount);
        var change = Assert.Single(diff.ChangedPanels);
        Assert.Equal(2, change.PanelNumber);
        Assert.Equal("second-2.png", change.NewImagePath);
        Assert.Equal("first-2.png", change.PreviousImagePath);
    }

    [Fact]
    public async Task GetStatus_SurfacesDraftNumberAndWhetherThereIsAPreviousDraft()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");
        var service = NewService();
        var first = await service.CreateFromDocAsync("doc-1", "Book.gdoc", "token", null, null, CancellationToken.None);
        var second = await service.CreateNextDraftAsync(first, "doc-2", "Book.gdoc", "token", null, null, CancellationToken.None);

        var firstStatus = await service.GetStatusAsync(first, CancellationToken.None);
        var secondStatus = await service.GetStatusAsync(second!.Value, CancellationToken.None);

        Assert.Equal(1, firstStatus!.DraftNumber);
        Assert.False(firstStatus.HasPreviousDraft);
        Assert.Equal(2, secondStatus!.DraftNumber);
        Assert.True(secondStatus.HasPreviousDraft);
    }

    [Fact]
    public async Task GetDraftDiff_OfAFirstDraft_IsEmpty()
    {
        docs.Setup(d => d.FetchText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");
        var id = await NewService().CreateFromDocAsync("doc", "Book.gdoc", "token", null, null, CancellationToken.None);

        var diff = await NewService().GetDraftDiffAsync(id, CancellationToken.None);

        Assert.Null(diff.PreviousDraftId);
        Assert.Empty(diff.ResolvedFindings);
        Assert.Empty(diff.NewFindings);
        Assert.Empty(diff.ChangedPanels);
        Assert.Equal(0, diff.ReusablePanelCount);
    }

    // Seeds one draft: the novel row, its source text, a single volume/page carrying the given panels
    // (panelNumber, source start, source end, image path), and a consolidated report.
    private Guid SeedDraft(
        Guid projectId, int draftNumber, Guid? previousDraftId, string fullText,
        BookAnalysisResult report, params (int PanelNumber, int Start, int End, string Image)[] panels)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Quest", GoogleDocId = "doc",
            ProjectId = projectId, DraftNumber = draftNumber, PreviousDraftId = previousDraftId,
            Stage = ProjectStage.Completed, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        novel.Source = new SourceText { NovelId = novel.Id, FullText = fullText, CharacterCount = fullText.Length };

        var volume = new Volume { NovelId = novel.Id, Number = 1, Title = "Volume One" };
        var page = new Page { VolumeId = volume.Id, ChapterNumber = 1, PageNumber = 1 };
        foreach (var (panelNumber, start, end, image) in panels)
            page.Panels.Add(new Panel
            {
                PageId = page.Id, PanelNumber = panelNumber,
                SourceStartOffset = start, SourceEndOffset = end,
                Status = WorkStatus.Completed, ImagePath = image
            });
        volume.Pages.Add(page);

        novel.Analysis = new BookAnalysis
        {
            NovelId = novel.Id,
            ConsolidatedJson = JsonSerializer.Serialize(report),
            PdfStatus = WorkStatus.Completed
        };

        context.Novels.Add(novel);
        context.Volumes.Add(volume);
        context.SaveChanges();
        return novel.Id;
    }

    [Fact]
    public async Task CancelAsync_MarksNovelCancelled_SoItNoLongerCountsAsActiveWork()
    {
        var id = SeedNovel(ProjectStage.BuildingDesignBible);

        await NewService().CancelAsync(id, CancellationToken.None);

        using var context = database.NewContext();
        Assert.Equal(ProjectStage.Cancelled, context.Novels.Single(n => n.Id == id).Stage);
        Assert.False(await NewService().HasActiveWorkAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CancelAsync_LeavesAlreadyCompletedNovelUntouched()
    {
        var id = SeedNovel(ProjectStage.Completed);

        await NewService().CancelAsync(id, CancellationToken.None);

        using var context = database.NewContext();
        Assert.Equal(ProjectStage.Completed, context.Novels.Single(n => n.Id == id).Stage);
    }

    [Fact]
    public async Task DeleteAsync_RemovesNovel_AllChildRows_AndOutputFiles()
    {
        var id = SeedNovelWithChildren();

        // A stand-in for the novel's rendered output on disk.
        var refPath = imageStore.Save($"{id}/refs/{Guid.NewGuid()}.png", SampleImages.Png());
        var pagePath = imageStore.Save($"{id}/pages/{Guid.NewGuid()}.png", SampleImages.Png());
        Assert.True(File.Exists(refPath));
        Assert.True(File.Exists(pagePath));

        await NewService().DeleteAsync(id, CancellationToken.None);

        using var context = database.NewContext();
        Assert.Empty(context.Novels.Where(n => n.Id == id));
        Assert.Empty(context.SourceTexts.Where(s => s.NovelId == id));
        Assert.Empty(context.Chunks.Where(c => c.NovelId == id));
        Assert.Empty(context.DesignRefs.Where(r => r.NovelId == id));
        Assert.Empty(context.Volumes.Where(v => v.NovelId == id));
        Assert.Empty(context.Pages);
        Assert.Empty(context.Panels);
        Assert.Empty(context.ChunkAnalyses.Where(a => a.NovelId == id));
        Assert.Empty(context.BookAnalyses.Where(a => a.NovelId == id));

        Assert.False(File.Exists(refPath));
        Assert.False(File.Exists(pagePath));
        Assert.False(Directory.Exists(Path.Combine(outputRoot, id.ToString())));
    }

    [Fact]
    public async Task DeleteAsync_IsANoOp_WhenTheNovelHasNoOutputDirectory()
    {
        var id = SeedNovel(ProjectStage.Created);

        // No files were ever written for this novel; deleting must not throw.
        await NewService().DeleteAsync(id, CancellationToken.None);

        using var context = database.NewContext();
        Assert.Empty(context.Novels.Where(n => n.Id == id));
    }

    private Guid SeedNovelWithChildren()
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Quest",
            GoogleDocId = "doc",
            Stage = ProjectStage.RenderingImages,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        novel.Source = new SourceText { NovelId = novel.Id, FullText = "text", CharacterCount = 4 };
        novel.Chunks.Add(new Chunk { NovelId = novel.Id, Ordinal = 0, Text = "chunk" });
        novel.DesignRefs.Add(new DesignRef { NovelId = novel.Id, Kind = ReferenceKind.Character, Name = "Hero" });
        novel.ChunkAnalyses.Add(new ChunkAnalysis { NovelId = novel.Id, Ordinal = 0 });
        novel.Analysis = new BookAnalysis { NovelId = novel.Id };

        var volume = new Volume { NovelId = novel.Id, Number = 1, Title = "Volume One" };
        var page = new Page { VolumeId = volume.Id, ChapterNumber = 1, PageNumber = 1 };
        page.Panels.Add(new Panel { PageId = page.Id, PanelNumber = 1, VisualPrompt = "Hero" });
        volume.Pages.Add(page);
        novel.Volumes.Add(volume);

        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    private static DiagnosticsContext Context(bool claudeSet = true, bool geminiSet = true) =>
        new(ClaudeKeySet: claudeSet, GeminiKeySet: geminiSet, GoogleAccount: "me@example.com",
            LastRunOutcome: "MoreWorkPending", LastRunAtUtc: Start, MigrationError: null);

    [Fact]
    public async Task DiagnosticsReport_SurfacesStage_FailedCounts_AndUnitErrors()
    {
        SeedTroubledNovel();

        var report = await NewService().BuildDiagnosticsReportAsync(Context(geminiSet: false), CancellationToken.None);

        Assert.Contains("Gemini key: MISSING", report);
        Assert.Contains("me@example.com", report);
        Assert.Contains("Stage: BuildingDesignBible", report);
        Assert.Contains("Novel error: the whole novel is unhappy", report);
        // 1 of 3 sheets done, 2 failed — and the failing reason is sampled with its count.
        Assert.Contains("Reference sheets: 1/3 done, 2 failed", report);
        Assert.Contains("no image in the Gemini response", report);
        Assert.Contains("×2", report);
    }

    [Fact]
    public async Task DiagnosticsReport_CleanProject_ReportsNoErrors()
    {
        SeedNovel(ProjectStage.Created);

        var report = await NewService().BuildDiagnosticsReportAsync(Context(), CancellationToken.None);

        Assert.Contains("Projects: 1", report);
        Assert.DoesNotContain("failed", report);
        Assert.DoesNotContain("Novel error", report);
    }

    [Fact]
    public async Task RetryAsync_ClearsStallAndMakesStuckWorkDueAgain()
    {
        Guid novelId;
        using (var context = database.NewContext())
        {
            var novel = new Novel
            {
                Title = "Quest", GoogleDocId = "doc", Stage = ProjectStage.Storyboarding,
                StoryboardAttempts = 6, LastError = "Socket closed",
                CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
            };
            novel.Chunks.Add(new Chunk
            {
                NovelId = novel.Id, Ordinal = 0, Text = "chunk",
                AnalysisStatus = WorkStatus.NeedsRetry, Attempts = 3,
                NextAttemptAtUtc = clock.UtcNow.AddHours(4), LastError = "429"
            });
            novel.DesignRefs.Add(new DesignRef
            {
                NovelId = novel.Id, Kind = ReferenceKind.Character, Name = "Hero",
                ReferenceSheetStatus = WorkStatus.Failed, Attempts = 6,
                NextAttemptAtUtc = clock.UtcNow.AddHours(4), LastError = "boom"
            });
            context.Novels.Add(novel);
            context.SaveChanges();
            novelId = novel.Id;
        }

        await NewService().RetryAsync(novelId, CancellationToken.None);

        using (var context = database.NewContext())
        {
            var novel = context.Novels.Single(n => n.Id == novelId);
            Assert.Null(novel.LastError);
            Assert.Equal(0, novel.StoryboardAttempts);

            var chunk = context.Chunks.Single(c => c.NovelId == novelId);
            Assert.Equal(WorkStatus.Pending, chunk.AnalysisStatus);
            Assert.Equal(0, chunk.Attempts);
            Assert.Null(chunk.NextAttemptAtUtc);
            Assert.Null(chunk.LastError);

            var reference = context.DesignRefs.Single(d => d.NovelId == novelId);
            Assert.Equal(WorkStatus.Pending, reference.ReferenceSheetStatus);
            Assert.Equal(0, reference.Attempts);
            Assert.Null(reference.NextAttemptAtUtc);
        }
    }

    [Fact]
    public async Task RetryAsync_RevivesFailedVolumeBinding_AndFailedAnalysis()
    {
        Guid novelId;
        using (var context = database.NewContext())
        {
            var novel = new Novel
            {
                Title = "Quest", GoogleDocId = "doc", Stage = ProjectStage.AssemblingVolumes,
                CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
            };
            novel.Volumes.Add(new Volume
            {
                NovelId = novel.Id, Number = 1, Title = "V1",
                PdfStatus = WorkStatus.Failed, Attempts = 6, PdfPageCursor = 2,
                PdfPath = "/tmp/partial.pdf", NextAttemptAtUtc = clock.UtcNow.AddHours(4),
                LastError = "binding boom"
            });
            novel.Analysis = new BookAnalysis { NovelId = novel.Id, PdfStatus = WorkStatus.Failed };
            context.Novels.Add(novel);
            context.SaveChanges();
            novelId = novel.Id;
        }

        await NewService().RetryAsync(novelId, CancellationToken.None);

        using (var context = database.NewContext())
        {
            // The failed binding is revived AND restarted from scratch (a partial PDF may be corrupt).
            var volume = context.Volumes.Single(v => v.NovelId == novelId);
            Assert.Equal(WorkStatus.Pending, volume.PdfStatus);
            Assert.Equal(0, volume.Attempts);
            Assert.Equal(0, volume.PdfPageCursor);
            Assert.Null(volume.PdfPath);
            Assert.Null(volume.NextAttemptAtUtc);
            Assert.Null(volume.LastError);

            // The failed analysis render resets too, so the report rebuilds once the volume binds.
            var analysis = context.BookAnalyses.Single(a => a.NovelId == novelId);
            Assert.Equal(WorkStatus.Pending, analysis.PdfStatus);
        }
    }

    [Fact]
    public async Task RetryAsync_LeavesCancelledProjectsAlone()
    {
        var id = SeedNovel(ProjectStage.Cancelled);

        await NewService().RetryAsync(id, CancellationToken.None);

        using var context = database.NewContext();
        Assert.Equal(ProjectStage.Cancelled, context.Novels.Single(n => n.Id == id).Stage);
    }

    // A Cancelled novel stopped on a prohibited/IP block: its offending panel carries the provider's
    // block message (exactly as the render loop leaves it), plus a reference and art style so the
    // reconstructed prompt in the report is deterministic. The panel status defaults to Pending, but the
    // render loop can also cancel on a panel that was mid-retry — pass NeedsRetry to seed that case.
    private (Guid NovelId, Guid PanelId) SeedCancelledNovelWithProhibitedPanel(
        int panelNumber = 4, WorkStatus panelStatus = WorkStatus.Pending)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Palxinota", GoogleDocId = "doc", ArtStyle = "noir",
            Stage = ProjectStage.Cancelled,
            LastError = "Cancelled: panel 4 was blocked as prohibited or copyrighted content, " +
                        "which relaxing the safety settings cannot bypass.",
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        var volume = new Volume { NovelId = novel.Id, Number = 1, Title = "V1" };
        var page = new Page { VolumeId = volume.Id, ChapterNumber = 1, PageNumber = 1 };
        var panel = new Panel
        {
            PageId = page.Id, PanelNumber = panelNumber,
            VisualPrompt = "a recognizable trademarked hero", ReferenceNamesJson = "[\"Hero\"]",
            Status = panelStatus,
            Attempts = panelStatus == WorkStatus.NeedsRetry ? 2 : 0,
            NextAttemptAtUtc = panelStatus == WorkStatus.NeedsRetry ? clock.UtcNow.AddMinutes(1) : null,
            LastError = "Gemini refused the image as IMAGE_SAFETY (PROHIBITED_CONTENT)"
        };
        page.Panels.Add(panel);
        volume.Pages.Add(page);
        novel.Volumes.Add(volume);
        context.Novels.Add(novel);
        context.SaveChanges();
        return (novel.Id, panel.Id);
    }

    [Fact]
    public async Task BlockedPanelReport_IncludesReconstructedPromptReasonAndReferences()
    {
        SeedCancelledNovelWithProhibitedPanel();

        var report = await NewService().BuildBlockedPanelReportAsync(CancellationToken.None);

        Assert.Contains("Panel 4", report);
        // The exact prompt the panel was rendered with, reconstructed via the shared PanelImagePrompt.
        Assert.Contains("Comic panel in this style: noir. a recognizable trademarked hero.", report);
        Assert.Contains("Match each character's and setting's appearance", report);
        Assert.Contains("PROHIBITED_CONTENT", report);
        Assert.Contains("Hero", report);
    }

    [Fact]
    public async Task BlockedPanelReport_ReportsNothing_WhenNoBlockedPanels()
    {
        SeedNovel(ProjectStage.RenderingImages);

        var report = await NewService().BuildBlockedPanelReportAsync(CancellationToken.None);

        Assert.Contains("No blocked panels found.", report);
    }

    [Fact]
    public async Task ReworkBlockedPanelAndResumeAsync_RevivesCancelledNovel_AndResetsTheReworkedPanel()
    {
        var (novelId, panelId) = SeedCancelledNovelWithProhibitedPanel();

        var reworked = await NewService().ReworkBlockedPanelAndResumeAsync(
            novelId, "a generic armored knight, an original design", CancellationToken.None);

        Assert.Equal(4, reworked);
        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == novelId);
        Assert.Equal(ProjectStage.RenderingImages, novel.Stage);
        Assert.Null(novel.LastError);

        var panel = context.Panels.Single(p => p.Id == panelId);
        Assert.Equal("a generic armored knight, an original design", panel.VisualPrompt);
        Assert.Equal(WorkStatus.Pending, panel.Status);
        Assert.Null(panel.LastError);
        Assert.Equal(0, panel.Attempts);
        Assert.Null(panel.NextAttemptAtUtc);
        Assert.False(panel.Skipped);
    }

    [Fact]
    public async Task ReworkBlockedPanelAndResumeAsync_IsNoOp_WhenNovelNotCancelled()
    {
        var id = SeedNovel(ProjectStage.RenderingImages);

        var reworked = await NewService().ReworkBlockedPanelAndResumeAsync(id, "x", CancellationToken.None);

        Assert.Null(reworked);
        using var context = database.NewContext();
        Assert.Equal(ProjectStage.RenderingImages, context.Novels.Single(n => n.Id == id).Stage);
    }

    [Fact]
    public async Task ReworkBlockedPanelAndResumeAsync_IsNoOp_WhenCancelledButNoOffendingPanel()
    {
        var id = SeedNovel(ProjectStage.Cancelled);

        var reworked = await NewService().ReworkBlockedPanelAndResumeAsync(id, "x", CancellationToken.None);

        Assert.Null(reworked);
        using var context = database.NewContext();
        Assert.Equal(ProjectStage.Cancelled, context.Novels.Single(n => n.Id == id).Stage);
    }

    [Fact]
    public async Task GetProhibitedPanelAsync_ReturnsOffendingPanelPromptAndReason()
    {
        var (novelId, panelId) = SeedCancelledNovelWithProhibitedPanel();

        var panel = await NewService().GetProhibitedPanelAsync(novelId, CancellationToken.None);

        Assert.NotNull(panel);
        Assert.Equal(panelId, panel!.PanelId);
        Assert.Equal(4, panel.PanelNumber);
        Assert.Equal("a recognizable trademarked hero", panel.VisualPrompt);
        Assert.Contains("PROHIBITED_CONTENT", panel.Reason);
    }

    // The render loop cancels on the first block it hits — including on a panel that was mid-retry, which
    // leaves the offending panel in NeedsRetry rather than Pending. The rework lookup must still find it,
    // or the Rework button silently does nothing (no prompt to edit, no fix).
    [Fact]
    public async Task GetProhibitedPanelAsync_FindsOffendingPanel_LeftRetrying()
    {
        var (novelId, panelId) = SeedCancelledNovelWithProhibitedPanel(panelStatus: WorkStatus.NeedsRetry);

        var panel = await NewService().GetProhibitedPanelAsync(novelId, CancellationToken.None);

        Assert.NotNull(panel);
        Assert.Equal(panelId, panel!.PanelId);
        Assert.Equal("a recognizable trademarked hero", panel.VisualPrompt);
    }

    // A lower-numbered panel parked on a transient failure (NeedsRetry) must not be mistaken for the
    // prohibited panel the loop settled into Pending as it cancelled — reworking the wrong panel would
    // leave the real block in place and re-cancel the book.
    [Fact]
    public async Task GetProhibitedPanelAsync_PrefersTheSettledPanel_OverALowerRetryingStraggler()
    {
        var (novelId, prohibitedPanelId) = SeedCancelledNovelWithProhibitedPanel(panelNumber: 7);
        using (var context = database.NewContext())
        {
            var pageId = context.Panels.Single(p => p.Id == prohibitedPanelId).PageId;
            context.Panels.Add(new Panel
            {
                PageId = pageId, PanelNumber = 3, VisualPrompt = "a transient straggler",
                ReferenceNamesJson = "[]", Status = WorkStatus.NeedsRetry, Attempts = 1,
                NextAttemptAtUtc = clock.UtcNow.AddMinutes(1), LastError = "429"
            });
            context.SaveChanges();
        }

        var panel = await NewService().GetProhibitedPanelAsync(novelId, CancellationToken.None);

        Assert.NotNull(panel);
        Assert.Equal(prohibitedPanelId, panel!.PanelId);
        Assert.Equal(7, panel.PanelNumber);
    }

    [Fact]
    public async Task ReworkBlockedPanelAndResumeAsync_RevivesNovel_WhenOffendingPanelWasRetrying()
    {
        var (novelId, panelId) = SeedCancelledNovelWithProhibitedPanel(panelStatus: WorkStatus.NeedsRetry);

        var reworked = await NewService().ReworkBlockedPanelAndResumeAsync(
            novelId, "a generic armored knight, an original design", CancellationToken.None);

        Assert.Equal(4, reworked);
        using var context = database.NewContext();
        Assert.Equal(ProjectStage.RenderingImages, context.Novels.Single(n => n.Id == novelId).Stage);

        var panel = context.Panels.Single(p => p.Id == panelId);
        Assert.Equal("a generic armored knight, an original design", panel.VisualPrompt);
        Assert.Equal(WorkStatus.Pending, panel.Status);
        Assert.Null(panel.LastError);
        Assert.Equal(0, panel.Attempts);
        Assert.Null(panel.NextAttemptAtUtc);
    }

    private void SeedTroubledNovel()
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Quest",
            GoogleDocId = "doc",
            Stage = ProjectStage.BuildingDesignBible,
            LastError = "the whole novel is unhappy",
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        novel.DesignRefs.Add(new DesignRef
        {
            NovelId = novel.Id, Kind = ReferenceKind.Character, Name = "Hero",
            ReferenceSheetStatus = WorkStatus.Failed, LastError = "no image in the Gemini response"
        });
        novel.DesignRefs.Add(new DesignRef
        {
            NovelId = novel.Id, Kind = ReferenceKind.Character, Name = "Villain",
            ReferenceSheetStatus = WorkStatus.Failed, LastError = "no image in the Gemini response"
        });
        novel.DesignRefs.Add(new DesignRef
        {
            NovelId = novel.Id, Kind = ReferenceKind.Setting, Name = "Castle",
            ReferenceSheetStatus = WorkStatus.Completed
        });
        context.Novels.Add(novel);
        context.SaveChanges();
    }
}
