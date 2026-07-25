using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Model;
using Storyboarder.Domain.Pipeline;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.Imaging;
using Storyboarder.Infrastructure.Pipeline;
using Storyboarder.Infrastructure.Services;
using Storyboarder.Tests.Support;
using Xunit;

namespace Storyboarder.Tests.Pipeline;

// Behavioural spec for content-blocked panel rendering: a safety block parks the panel for the user's
// OK (without wedging the book), an approval relaxes only the flagged category, the model's floor is
// handled by restaging via Claude and then a placeholder, a Filtered (IMAGE_SAFETY) block restages and
// degrades to a placeholder without approval or cancelling, and a prohibited/IP block cancels the book.
public class PanelContentApprovalTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] DangerousCategory = ["HARM_CATEGORY_DANGEROUS_CONTENT"];

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "storyboarder-approval", Guid.NewGuid().ToString("N"));

    private readonly Mock<IClaudeAnalysisClient> claude = new();
    private readonly Mock<IGeminiImageClient> gemini = new();

    // Archive settings and a fake Drive for the archive stage; null/off by default so every
    // existing test drives the unarchived path.
    private ArchiveOptions? archiveOptions;
    private IDriveArchive? driveArchive;

    public PanelContentApprovalTests()
    {
        claude.Setup(c => c.ExtractEntities(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesignBibleFragment(
                [new EntityMention("Hero", "a brave knight in silver armor")], []));

        claude.Setup(c => c.Storyboard(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoryboardSegment(
                [new VolumeDraft(1, "Volume One",
                    [new PageDraft(1, [new PanelDraft("Hero fights the duel", "At last.", null, ["Hero"])])])],
                "Hero fought."));

        claude.Setup(c => c.AnalyzeChapter(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChapterNotes(
                "Chapter 1", ["Hero"], ["fights"], [], [], [], [], ["Vivid"], [], [], [], [], []));

        claude.Setup(c => c.ConsolidateAnalysis(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookAnalysisResult(
                "A duel.", "A hero duels.", [], [], [], [], [], [],
                [new Review("editor", "ok", [], [], "revise")], [], [], "positioning", [], [], [], []));

        claude.Setup(c => c.ConsolidateCraft(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CraftReport.Empty);

        claude.Setup(c => c.RewritePromptToImply(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("The blow lands off-frame; the crowd recoils.");

        claude.Setup(c => c.CheckFacts(
                It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new FactCheck("Paris is in France", "Correct", "Confirmed.", "Chapter 1")]);

        gemini.Setup(g => g.GenerateReferenceSheet(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => SampleImages.Png());
    }

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }

    private GraphicNovelProcessor NewProcessor() => new(
        database,
        claude.Object,
        gemini.Object,
        new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot })),
        new SkiaPageComposer(Options.Create(new PageLayoutOptions())),
        new PdfVolumeComposer(),
        new AnalysisPdfComposer(new PdfVolumeComposer(), new AnalysisDocumentRenderer()),
        clock,
        Options.Create(new RateLimitOptions
        {
            ClaudeRequestsPerMinute = 1000, ClaudeRequestsPerDay = 100000,
            GeminiRequestsPerMinute = 1000, GeminiRequestsPerDay = 100000,
            BaseDelaySeconds = 4, MaxDelaySeconds = 60, MaxAttempts = 6
        }),
        Options.Create(new PageLayoutOptions()),
        Options.Create(archiveOptions ?? new ArchiveOptions()),
        NullLogger<GraphicNovelProcessor>.Instance,
        driveArchive);

    private ProjectService NewProjectService() => new(
        database,
        Mock.Of<IGoogleDocsClient>(),
        new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot })),
        clock,
        Options.Create(new RateLimitOptions { MaxAttempts = 6 }));

    private void BlockPanelRenders(ImageBlockReason reason, IReadOnlyList<string>? categories = null) =>
        gemini.Setup(g => g.GeneratePanel(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ImageContentBlockedException("blocked", reason, categories ?? DangerousCategory));

    private Guid SeedNovel(string? text = null)
    {
        text ??= "A short tale. The hero fought a brutal duel at the castle gate.";
        var novel = new Novel { Title = "Duel", GoogleDocId = "doc", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        novel.Source = new SourceText { NovelId = novel.Id, FullText = text, CharacterCount = text.Length };
        using var context = database.NewContext();
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    private ProjectStage CurrentStage(Guid novelId)
    {
        using var context = database.NewContext();
        return context.Novels.Single(n => n.Id == novelId).Stage;
    }

    private Panel SinglePanel(Guid novelId)
    {
        using var context = database.NewContext();
        var volumeIds = context.Volumes.Where(v => v.NovelId == novelId).Select(v => v.Id).ToList();
        var pageIds = context.Pages.Where(p => volumeIds.Contains(p.VolumeId)).Select(p => p.Id).ToList();
        return context.Panels.Where(p => pageIds.Contains(p.PageId)).OrderBy(p => p.PanelNumber).First();
    }

    // Advances the pipeline until it reaches a terminal stage or goes Idle (nothing runnable — e.g. a
    // panel parked awaiting the user's approval). A fresh processor per cycle proves DB-only resume.
    private async Task<ProjectStage> Drive(Guid novelId, int maxCycles = 400)
    {
        for (int i = 0; i < maxCycles; i++)
        {
            var stage = CurrentStage(novelId);
            if (stage is ProjectStage.Completed or ProjectStage.Failed or ProjectStage.Cancelled)
                return stage;

            var result = await NewProcessor().ProcessNextAsync(CancellationToken.None);
            if (result.Outcome == ProcessOutcome.Idle)
                return CurrentStage(novelId);
            if (result.Outcome == ProcessOutcome.Waiting)
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
        }
        return CurrentStage(novelId);
    }

    [Fact]
    public async Task SafetyBlockedPanel_ParksForApproval_WithoutFailingTheBook()
    {
        BlockPanelRenders(ImageBlockReason.Safety);
        var novelId = SeedNovel();

        var stage = await Drive(novelId);

        // The book pauses at rendering — not Failed, not Cancelled — with the panel awaiting the user.
        Assert.Equal(ProjectStage.RenderingImages, stage);
        var panel = SinglePanel(novelId);
        Assert.Equal(WorkStatus.AwaitingApproval, panel.Status);
        Assert.Contains("HARM_CATEGORY_DANGEROUS_CONTENT", panel.BlockedCategoriesJson);

        var status = await NewProjectService().GetStatusAsync(novelId, CancellationToken.None);
        Assert.Equal(1, status!.PendingApprovals);
        Assert.True(status.NeedsAttention);

        var blocked = await NewProjectService().GetBlockedPanelsAsync(novelId, CancellationToken.None);
        Assert.Equal("HARM_CATEGORY_DANGEROUS_CONTENT", Assert.Single(Assert.Single(blocked).Categories));
    }

    [Fact]
    public async Task ApprovedPanel_RelaxesFlaggedCategory_AndRendersToCompletion()
    {
        BlockPanelRenders(ImageBlockReason.Safety);
        // The approved (relaxed) re-render succeeds.
        IReadOnlyCollection<string>? relaxedSeen = null;
        gemini.Setup(g => g.GeneratePanel(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, IReadOnlyList<byte[]> _, double? _, IReadOnlyCollection<string> relaxed, CancellationToken _) =>
            {
                relaxedSeen = relaxed;
                return Task.FromResult(SampleImages.Png());
            });

        var novelId = SeedNovel();
        Assert.Equal(ProjectStage.RenderingImages, await Drive(novelId));

        var approved = await NewProjectService().ApprovePanelRenderAsync(SinglePanel(novelId).Id, CancellationToken.None);
        Assert.True(approved);

        Assert.Equal(ProjectStage.Completed, await Drive(novelId));
        // Only the flagged category was relaxed on the re-render.
        Assert.Equal(new[] { "HARM_CATEGORY_DANGEROUS_CONTENT" }, relaxedSeen);
        Assert.Equal(WorkStatus.Completed, SinglePanel(novelId).Status);
    }

    [Fact]
    public async Task ApprovedPanel_HittingTheFloor_IsRestagedByClaude_ThenRenders()
    {
        BlockPanelRenders(ImageBlockReason.Safety);
        // The relaxed render still hits the model's floor on the first try, then succeeds once the
        // prompt has been restaged to imply the content.
        var relaxedCalls = 0;
        gemini.Setup(g => g.GeneratePanel(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                relaxedCalls++;
                return relaxedCalls == 1
                    ? Task.FromException<byte[]>(new ImageContentBlockedException("floor", ImageBlockReason.Safety, DangerousCategory))
                    : Task.FromResult(SampleImages.Png());
            });

        var novelId = SeedNovel();
        Assert.Equal(ProjectStage.RenderingImages, await Drive(novelId));
        await NewProjectService().ApprovePanelRenderAsync(SinglePanel(novelId).Id, CancellationToken.None);

        Assert.Equal(ProjectStage.Completed, await Drive(novelId));
        claude.Verify(c => c.RewritePromptToImply(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        var panel = SinglePanel(novelId);
        Assert.True(panel.PromptRewrittenForSafety);
        Assert.Equal("The blow lands off-frame; the crowd recoils.", panel.VisualPrompt);
    }

    [Fact]
    public async Task RestagedPanel_StillBlocked_FallsBackToPlaceholder_SoTheBookFinishes()
    {
        BlockPanelRenders(ImageBlockReason.Safety);
        // Even the relaxed, restaged render stays blocked — the book must still finish via a placeholder.
        gemini.Setup(g => g.GeneratePanel(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ImageContentBlockedException("still blocked", ImageBlockReason.Safety, DangerousCategory));

        var novelId = SeedNovel();
        Assert.Equal(ProjectStage.RenderingImages, await Drive(novelId));
        await NewProjectService().ApprovePanelRenderAsync(SinglePanel(novelId).Id, CancellationToken.None);

        Assert.Equal(ProjectStage.Completed, await Drive(novelId));
        var panel = SinglePanel(novelId);
        Assert.Equal(WorkStatus.Completed, panel.Status);
        Assert.NotNull(panel.ImagePath);
        Assert.True(File.Exists(panel.ImagePath));
        Assert.Contains("placeholder", panel.LastError);
    }

    [Fact]
    public async Task FilteredPanel_IsRestagedByClaude_ThenRenders_WithoutApprovalOrCancelling()
    {
        // Gemini's non-configurable image-safety filter (IMAGE_SAFETY) blocks the first render. It can't
        // be relaxed and there's nothing to approve, so the panel is restaged to imply the content and
        // re-rendered automatically — never parked for approval, never cancelling the book.
        var calls = 0;
        gemini.Setup(g => g.GeneratePanel(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls++;
                return calls == 1
                    ? Task.FromException<byte[]>(new ImageContentBlockedException("filtered", ImageBlockReason.Filtered))
                    : Task.FromResult(SampleImages.Png());
            });

        var novelId = SeedNovel();

        Assert.Equal(ProjectStage.Completed, await Drive(novelId));
        claude.Verify(c => c.RewritePromptToImply(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        var panel = SinglePanel(novelId);
        Assert.Equal(WorkStatus.Completed, panel.Status);
        Assert.True(panel.PromptRewrittenForSafety);
        // No approval was involved — the relaxed-render path is never entered for a Filtered block.
        Assert.False(panel.ApprovedForRelaxedRender);
        Assert.Equal("The blow lands off-frame; the crowd recoils.", panel.VisualPrompt);
    }

    [Fact]
    public async Task FilteredPanel_StillBlockedAfterRestage_FallsBackToPlaceholder_WithoutCancelling()
    {
        // Even the restaged prompt stays filtered — the single panel degrades to a placeholder so the
        // book still finishes, rather than the whole conversion being cancelled.
        BlockPanelRenders(ImageBlockReason.Filtered, []);
        var novelId = SeedNovel();

        Assert.Equal(ProjectStage.Completed, await Drive(novelId));
        var panel = SinglePanel(novelId);
        Assert.Equal(WorkStatus.Completed, panel.Status);
        Assert.NotNull(panel.ImagePath);
        Assert.True(File.Exists(panel.ImagePath));
        Assert.Contains("placeholder", panel.LastError);
    }

    [Fact]
    public async Task ProhibitedPanel_ParksThePanel_WithoutCancellingTheBook()
    {
        // A prohibited block used to cancel the whole conversion, which lost the owner two books after
        // hours of rendering. It must now restage the prompt first and, if that is still refused, park
        // only the offending panel — the novel stays alive with every other panel intact.
        BlockPanelRenders(ImageBlockReason.Prohibited, []);
        var novelId = SeedNovel();

        var stage = await Drive(novelId);

        Assert.NotEqual(ProjectStage.Cancelled, stage);
        Assert.NotEqual(ProjectStage.Failed, stage);
        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == novelId);
        Assert.NotEqual(ProjectStage.Cancelled, novel.Stage);

        var panel = SinglePanel(novelId);
        Assert.Equal(WorkStatus.AwaitingApproval, panel.Status);
        // Rewording was tried before giving up, and there are no harm categories to relax — prohibited
        // content is not adjustable, which is what tells the rework flow this panel is its business.
        Assert.True(panel.PromptRewrittenForSafety);
        Assert.Null(panel.BlockedCategoriesJson);

        // Surfaced to the user so the book doesn't look silently stalled — as a panel to REWORK, not one
        // to approve: there is no adjustable category here, so the approve-and-relax sheet would have
        // nothing to offer and must not claim it has something to review.
        var status = await NewProjectService().GetStatusAsync(novelId, CancellationToken.None);
        Assert.Equal(1, status!.ProhibitedPanels);
        Assert.Equal(0, status.PendingApprovals);
        Assert.True(status.NeedsAttention);
        Assert.Empty(await NewProjectService().GetBlockedPanelsAsync(novelId, CancellationToken.None));
    }

    [Fact]
    public async Task ProhibitedPanel_CanBeReworked_AndTheBookResumesAndFinishes()
    {
        BlockPanelRenders(ImageBlockReason.Prohibited, []);
        var novelId = SeedNovel();
        // The panel parks awaiting the user; the book is not cancelled.
        Assert.NotEqual(ProjectStage.Cancelled, await Drive(novelId));

        // The user reworks the art direction to avoid the gated content; the reworked prompt renders clean.
        gemini.Setup(g => g.GeneratePanel(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => SampleImages.Png());

        var reworked = await NewProjectService().ReworkBlockedPanelAndResumeAsync(
            novelId, "a generic knight, an original design", CancellationToken.None);
        Assert.NotNull(reworked);

        // The book runs through to the end — the panels rendered before the block are kept, and the
        // reworked panel is rendered (not skipped).
        Assert.Equal(ProjectStage.Completed, await Drive(novelId));
        var panel = SinglePanel(novelId);
        Assert.Equal(WorkStatus.Completed, panel.Status);
        Assert.False(panel.Skipped);
        Assert.Equal("a generic knight, an original design", panel.VisualPrompt);
        Assert.NotNull(panel.ImagePath);
    }

    [Fact]
    public async Task ProhibitedBlock_OnARetryingPanel_ParksItReworkable_WithoutCancelling()
    {
        // A panel that already had a transient failure (NeedsRetry, now due) hits a prohibited block on
        // its retry render. The book must not cancel, and once rewording has been tried and refused the
        // panel must be left in a state the rework flow can find and reset — not stranded in NeedsRetry
        // where GetProhibitedPanel can't see it, which is what made the Rework button silently do nothing.
        BlockPanelRenders(ImageBlockReason.Prohibited, []);
        var novelId = SeedRetryingRenderingNovel();

        var stage = await Drive(novelId);

        Assert.NotEqual(ProjectStage.Cancelled, stage);
        var panel = SinglePanel(novelId);
        Assert.Equal(WorkStatus.AwaitingApproval, panel.Status);
        // No retry schedule left dangling, so the rework lookup finds it deterministically.
        Assert.Null(panel.NextAttemptAtUtc);
        Assert.NotNull(panel.LastError);

        var prohibited = await NewProjectService().GetProhibitedPanelAsync(novelId, CancellationToken.None);
        Assert.NotNull(prohibited);
    }

    [Fact]
    public async Task SkippedPanel_IsLeftOut_AndTheBookFinishes()
    {
        BlockPanelRenders(ImageBlockReason.Safety);
        var novelId = SeedNovel();
        Assert.Equal(ProjectStage.RenderingImages, await Drive(novelId));

        var skipped = await NewProjectService().SkipPanelRenderAsync(SinglePanel(novelId).Id, CancellationToken.None);
        Assert.True(skipped);

        Assert.Equal(ProjectStage.Completed, await Drive(novelId));
        var panel = SinglePanel(novelId);
        Assert.True(panel.Skipped);
        Assert.Equal(WorkStatus.Completed, panel.Status);
        Assert.Null(panel.ImagePath);
    }

    [Fact]
    public async Task ANovelParkedForApproval_DoesNotBlockAnotherNovelFromProceeding()
    {
        // Older novel A is parked (its only panel awaiting approval); newer novel B has a renderable
        // panel. One cycle must skip past A and do B's work rather than idling on A.
        var novelA = SeedRenderingNovel(WorkStatus.AwaitingApproval, secondsOffset: 0);
        var novelB = SeedRenderingNovel(WorkStatus.Pending, secondsOffset: 10);
        gemini.Setup(g => g.GeneratePanel(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => SampleImages.Png());

        var result = await NewProcessor().ProcessNextAsync(CancellationToken.None);

        Assert.Equal(ProcessOutcome.DidWork, result.Outcome);
        Assert.Equal(WorkStatus.Completed, SinglePanel(novelB).Status);
        // A stayed parked, untouched.
        Assert.Equal(WorkStatus.AwaitingApproval, SinglePanel(novelA).Status);
    }

    [Fact]
    public async Task ProviderRateLimit_RecordsExpectedResumeTime_SurfacedInStatus()
    {
        var novelId = SeedRenderingNovel(WorkStatus.Pending, secondsOffset: 0);
        gemini.Setup(g => g.GeneratePanel(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RateLimitException("429", TimeSpan.FromMinutes(5)));

        var result = await NewProcessor().ProcessNextAsync(CancellationToken.None);

        Assert.Equal(ProcessOutcome.DidWork, result.Outcome);
        using (var ctx = database.NewContext())
        {
            var novel = ctx.Novels.Single(n => n.Id == novelId);
            Assert.NotNull(novel.RateLimitedUntilUtc);
            Assert.True(novel.RateLimitedUntilUtc > Start, "the resume time should be in the future");
        }

        // The status read model surfaces it (while it's still in the future) so the card can show it.
        var status = await NewProjectService().GetStatusAsync(novelId, CancellationToken.None);
        Assert.NotNull(status!.RateLimitedUntilUtc);
    }

    [Fact]
    public async Task RateLimitedNovel_DoesNotStarve_AYoungerNovelThatCanFinish()
    {
        // Reproduces The Disc stuck at "Writing the revision report…": one novel is parked on a rate-limit
        // wait (its panel NeedsRetry, not yet due), and a younger novel has already finished its analysis
        // PDF and only needs the final move to Completed. The worker must step past the waiting novel and
        // let the ready one finish — not return the older novel's wait and starve it every cycle.
        SeedRateLimitedRenderingNovel(secondsOffset: -100);
        var readyNovelId = SeedNovelReadyToCompleteAnalysis(secondsOffset: 0);

        var result = await NewProcessor().ProcessNextAsync(CancellationToken.None);
        Assert.Equal(ProcessOutcome.DidWork, result.Outcome);

        // The worker stepped past the waiting novel and did the ready one's work; a couple more cycles
        // carry it through the archive stage to the end.
        Assert.Equal(ProjectStage.Completed, await Drive(readyNovelId));
    }

    [Fact]
    public async Task ReportAlreadyRendered_Completes_WithoutBuyingACraftConsolidation()
    {
        // The upgrade path: a novel whose report PDF was rendered before the craft half of the reduce
        // existed carries CraftJson = null. Gating the craft call on that alone would bill a Claude
        // consolidation whose output could never reach the PDF already on disk, so the stage must read
        // "the report is rendered, therefore this stage is done" and simply advance.
        var novelId = SeedNovelReadyToCompleteAnalysis(secondsOffset: 0);

        // Drives rather than taking a single cycle: the book still has the archive stage to pass through,
        // which with archiving off is a straight-through transition.
        Assert.Equal(ProjectStage.Completed, await Drive(novelId));
        claude.Verify(c => c.ConsolidateCraft(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        claude.Verify(c => c.ConsolidateAnalysis(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<DesignBible>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private ProjectStage StageOf(Guid novelId)
    {
        using var context = database.NewContext();
        return context.Novels.Single(n => n.Id == novelId).Stage;
    }

    // Seeds a novel at RenderingImages whose only panel is awaiting a retry that isn't due yet, so the
    // render stage reports Waiting (a rate-limit / backoff window) rather than doing any work.
    private Guid SeedRateLimitedRenderingNovel(int secondsOffset)
    {
        var now = clock.UtcNow.AddSeconds(secondsOffset);
        var novel = new Novel
        {
            Title = "Rate limited", GoogleDocId = "doc", Stage = ProjectStage.RenderingImages,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        novel.Source = new SourceText { NovelId = novel.Id, FullText = "t", CharacterCount = 1 };
        var volume = new Volume { NovelId = novel.Id, Number = 1, Title = "V1" };
        var page = new Page { VolumeId = volume.Id, ChapterNumber = 1, PageNumber = 1, CompositionStatus = WorkStatus.Pending };
        page.Panels.Add(new Panel
        {
            PageId = page.Id, PanelNumber = 1, VisualPrompt = "p", ReferenceNamesJson = "[]",
            Status = WorkStatus.NeedsRetry, Attempts = 1, LastError = "429",
            NextAttemptAtUtc = clock.UtcNow.AddMinutes(5)
        });
        volume.Pages.Add(page);
        novel.Volumes.Add(volume);

        using var context = database.NewContext();
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    // Seeds a novel at ComposingAnalysis with every analysis unit done, the report consolidated, and the
    // analysis PDF already rendered — so the only work left is the transition to Completed.
    private Guid SeedNovelReadyToCompleteAnalysis(int secondsOffset)
    {
        var now = clock.UtcNow.AddSeconds(secondsOffset);
        var novel = new Novel
        {
            Title = "Ready", GoogleDocId = "doc", Stage = ProjectStage.ComposingAnalysis,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        novel.Source = new SourceText { NovelId = novel.Id, FullText = "t", CharacterCount = 1 };

        using var context = database.NewContext();
        context.Novels.Add(novel);
        context.ChunkAnalyses.Add(new ChunkAnalysis
        {
            NovelId = novel.Id, Ordinal = 0, Status = WorkStatus.Completed, NotesJson = "{}"
        });
        context.BookAnalyses.Add(new BookAnalysis
        {
            NovelId = novel.Id, ConsolidatedJson = "{}", PdfStatus = WorkStatus.Completed,
            PdfPath = "book-analysis.pdf"
        });
        context.SaveChanges();
        return novel.Id;
    }

    // Seeds a novel at RenderingImages whose only panel is a due retry (NeedsRetry with its next-attempt
    // time already past), so the render loop picks it up this cycle — the setup for a block that lands on
    // a mid-retry panel.
    private Guid SeedRetryingRenderingNovel()
    {
        var now = clock.UtcNow;
        var novel = new Novel
        {
            Title = "Retrying", GoogleDocId = "doc", Stage = ProjectStage.RenderingImages,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        novel.Source = new SourceText { NovelId = novel.Id, FullText = "t", CharacterCount = 1 };
        var volume = new Volume { NovelId = novel.Id, Number = 1, Title = "V1" };
        var page = new Page { VolumeId = volume.Id, ChapterNumber = 1, PageNumber = 1, CompositionStatus = WorkStatus.Pending };
        page.Panels.Add(new Panel
        {
            PageId = page.Id, PanelNumber = 1, VisualPrompt = "p", ReferenceNamesJson = "[]",
            Status = WorkStatus.NeedsRetry, Attempts = 1, LastError = "429",
            NextAttemptAtUtc = now.AddMinutes(-1)
        });
        volume.Pages.Add(page);
        novel.Volumes.Add(volume);

        using var context = database.NewContext();
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    // Seeds a novel already at RenderingImages with one volume/page/panel in the given state.
    private Guid SeedRenderingNovel(WorkStatus panelStatus, int secondsOffset)
    {
        var now = clock.UtcNow.AddSeconds(secondsOffset);
        var novel = new Novel
        {
            Title = "Seeded", GoogleDocId = "doc", Stage = ProjectStage.RenderingImages,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        novel.Source = new SourceText { NovelId = novel.Id, FullText = "t", CharacterCount = 1 };
        var volume = new Volume { NovelId = novel.Id, Number = 1, Title = "V1" };
        var page = new Page { VolumeId = volume.Id, ChapterNumber = 1, PageNumber = 1, CompositionStatus = WorkStatus.Pending };
        page.Panels.Add(new Panel
        {
            PageId = page.Id, PanelNumber = 1, VisualPrompt = "p", ReferenceNamesJson = "[]",
            Status = panelStatus,
            BlockedCategoriesJson = panelStatus == WorkStatus.AwaitingApproval ? "[\"HARM_CATEGORY_DANGEROUS_CONTENT\"]" : null
        });
        volume.Pages.Add(page);
        novel.Volumes.Add(volume);

        using var context = database.NewContext();
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }
}
