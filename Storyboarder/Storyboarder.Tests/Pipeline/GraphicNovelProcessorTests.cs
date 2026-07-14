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
using Storyboarder.Tests.Support;
using Xunit;

namespace Storyboarder.Tests.Pipeline;

public class GraphicNovelProcessorTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "storyboarder-e2e", Guid.NewGuid().ToString("N"));

    private readonly Mock<IClaudeAnalysisClient> claude = new();
    private readonly Mock<IGeminiImageClient> gemini = new();
    private int panelRenderCalls;
    private int chapterAnalysisCalls;

    public GraphicNovelProcessorTests()
    {
        claude.Setup(c => c.ExtractEntities(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesignBibleFragment(
                [new EntityMention("Hero", "a brave knight in silver armor")],
                [new EntityMention("Castle", "a grey stone fortress")]));

        claude.Setup(c => c.Storyboard(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoryboardSegment(
                [new VolumeDraft(1, "Volume One",
                    [new PageDraft(1, [new PanelDraft("Hero stands before the Castle", "At last.", null, ["Hero", "Castle"])])])],
                "Hero reached the castle."));

        // The first chapter-analysis call is rate-limited; the worker must reschedule and succeed on retry.
        claude.Setup(c => c.AnalyzeChapter(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                chapterAnalysisCalls++;
                return chapterAnalysisCalls == 1
                    ? Task.FromException<ChapterNotes>(new RateLimitException("429 from test"))
                    : Task.FromResult(new ChapterNotes(
                        ["Hero"], ["Hero walks to the castle"], ["Hero resolves to enter"], [], [], [], ["Vivid setting"], []));
            });

        claude.Setup(c => c.ConsolidateAnalysis(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookAnalysisResult(
                "A hero reaches a castle.", "The hero journeys to the castle and knocks.",
                [new CharacterProfile("Hero", "protagonist", "Grows from timid to resolute.", ["approaches: Castle"])],
                ["perseverance"], ["How did the gate open?"], ["Eye colour shifts"], ["Castle geography"], ["Slow open"],
                [new Review("developmental editor", "Promising but uneven.", ["Voice"], ["Pacing"], "Tighten the opening.")],
                [new RevisionItem("High", "Sagging middle", "Cut a subplot.")],
                ["Comparable Title"], "Upmarket fantasy for adult readers."));

        gemini.Setup(g => g.GenerateReferenceSheet(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => SampleImages.Png());

        // The first panel render is rate-limited; the worker must reschedule and succeed on retry.
        gemini.Setup(g => g.GeneratePanel(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                panelRenderCalls++;
                return panelRenderCalls == 1
                    ? Task.FromException<byte[]>(new RateLimitException("429 from test"))
                    : Task.FromResult(SampleImages.Png());
            });
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
        NullLogger<GraphicNovelProcessor>.Instance);

    private Guid SeedNovel(string? fullText = null)
    {
        var text = fullText ?? "A short tale. The hero walked to the castle and knocked on the gate.";
        var novel = new Novel { Title = "Quest", GoogleDocId = "doc", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        novel.Source = new SourceText
        {
            NovelId = novel.Id,
            FullText = text,
            CharacterCount = text.Length
        };
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

    [Fact]
    public async Task Storyboarding_slices_a_long_chunk_into_several_windows()
    {
        // A manuscript long enough to be a single design-bible chunk (well under the ~48K chunker
        // target) but several small storyboard windows. Each window is its own short Claude call: a
        // whole chunk adapted in one call runs long enough to be killed by the background worker's
        // time limit, which is what stalled the pipeline; slicing keeps every call short.
        var manuscript = string.Join("\n\n",
            Enumerable.Range(0, 120).Select(i =>
                $"Paragraph {i}: the hero walks onward through the long grey hall, thinking of home and the road ahead."));
        var novelId = SeedNovel(manuscript);

        await DriveToCompletion(novelId, _ => { });

        Assert.Equal(ProjectStage.Completed, CurrentStage(novelId));

        using var context = database.NewContext();
        var chunkCount = context.Chunks.Count(c => c.NovelId == novelId);
        var storyboardCalls = claude.Invocations
            .Count(i => i.Method.Name == nameof(IClaudeAnalysisClient.Storyboard));
        // The manuscript is a single design-bible chunk, yet storyboarding adapted it in more than one
        // window — proof the chunk was sliced rather than sent to Claude whole.
        Assert.Equal(1, chunkCount);
        Assert.True(storyboardCalls > 1,
            $"a single chunk should be storyboarded in several windows, but Storyboard was called {storyboardCalls} time(s)");
    }

    [Fact]
    public async Task DrivesNovel_AllTheWayToCompletedPdf_RecoveringFromRateLimit()
    {
        var novelId = SeedNovel();

        // A fresh processor every iteration proves the pipeline resumes purely from database state.
        for (int i = 0; i < 200; i++)
        {
            var result = await NewProcessor().ProcessNextAsync(CancellationToken.None);

            var stage = CurrentStage(novelId);
            if (stage is ProjectStage.Completed or ProjectStage.Failed)
                break;

            if (result.Outcome == ProcessOutcome.Waiting)
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
            else if (result.Outcome == ProcessOutcome.Idle)
                Assert.Fail($"Pipeline stalled in stage {stage} (last error surfaced via DB).");
        }

        Assert.Equal(ProjectStage.Completed, CurrentStage(novelId));
        Assert.True(panelRenderCalls >= 2, "The rate-limited first panel render should have been retried.");

        using var context = database.NewContext();
        var design = context.DesignRefs.Where(r => r.NovelId == novelId).ToList();
        Assert.Equal(2, design.Count);
        Assert.All(design, r => Assert.Equal(WorkStatus.Completed, r.ReferenceSheetStatus));

        var volume = Assert.Single(context.Volumes.Where(v => v.NovelId == novelId));
        Assert.Equal(WorkStatus.Completed, volume.PdfStatus);
        Assert.NotNull(volume.PdfPath);
        Assert.True(File.Exists(volume.PdfPath));

        // Every composed page also gets a pre-scaled library thumbnail.
        var composedPages = context.Pages.Where(p => p.CompositionStatus == WorkStatus.Completed).ToList();
        Assert.NotEmpty(composedPages);
        Assert.All(composedPages, p =>
        {
            Assert.NotNull(p.ThumbImagePath);
            Assert.True(File.Exists(p.ThumbImagePath));
        });
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(volume.PdfPath!)[..4]));

        Assert.True(chapterAnalysisCalls >= 2, "The rate-limited first chapter analysis should have been retried.");
        var analysis = Assert.Single(context.BookAnalyses.Where(a => a.NovelId == novelId));
        Assert.Equal(WorkStatus.Completed, analysis.PdfStatus);
        Assert.NotNull(analysis.PdfPath);
        Assert.True(File.Exists(analysis.PdfPath));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(analysis.PdfPath!)[..4]));
    }

    [Fact]
    public async Task ReRendersPanel_WhenItsImageFileVanishes_InsteadOfStalling()
    {
        var novelId = SeedNovel();
        var disrupted = false;
        var rendersBeforeDisruption = 0;

        // When a rendered panel's image disappears (cache eviction, a partial restore), page
        // composition would otherwise throw on the missing file and block the novel forever.
        await DriveToCompletion(novelId, stage =>
        {
            if (stage != ProjectStage.ComposingPages || disrupted)
                return;
            using var context = database.NewContext();
            var panel = context.Panels.First(p => p.Status == WorkStatus.Completed && p.ImagePath != null);
            Assert.True(File.Exists(panel.ImagePath!));
            File.Delete(panel.ImagePath!);
            rendersBeforeDisruption = panelRenderCalls;
            disrupted = true;
        });

        Assert.True(disrupted, "Expected the pipeline to reach ComposingPages so a panel image could be removed.");
        Assert.Equal(ProjectStage.Completed, CurrentStage(novelId));
        Assert.True(panelRenderCalls > rendersBeforeDisruption, "The panel with the missing image should have been re-rendered.");

        using var ctx = database.NewContext();
        var restored = ctx.Panels.First();
        Assert.Equal(WorkStatus.Completed, restored.Status);
        Assert.True(File.Exists(restored.ImagePath!));
    }

    [Fact]
    public async Task RecomposesPage_WhenComposedImageVanishes_BeforeVolumeAssembly()
    {
        var novelId = SeedNovel();
        var disrupted = false;

        await DriveToCompletion(novelId, stage =>
        {
            if (stage != ProjectStage.AssemblingVolumes || disrupted)
                return;
            using var context = database.NewContext();
            var page = context.Pages.First(p => p.CompositionStatus == WorkStatus.Completed && p.ComposedImagePath != null);
            File.Delete(page.ComposedImagePath!);
            disrupted = true;
        });

        Assert.True(disrupted, "Expected the pipeline to reach AssemblingVolumes so a composed page could be removed.");
        Assert.Equal(ProjectStage.Completed, CurrentStage(novelId));

        using var ctx = database.NewContext();
        var volume = ctx.Volumes.First(v => v.NovelId == novelId);
        Assert.Equal(WorkStatus.Completed, volume.PdfStatus);
        Assert.True(File.Exists(volume.PdfPath!));
        var page2 = ctx.Pages.First();
        Assert.True(File.Exists(page2.ComposedImagePath!));
    }

    [Fact]
    public async Task CompletesNovel_EvenWhenEveryReferenceSheetRenderFailsWithoutAnImage()
    {
        // The image model can return a response that carries no usable image (a text-only or
        // safety-blocked reply), which surfaces as a non-HTTP InvalidOperationException. That must be
        // retried and then tolerated — not left to escape the cycle and wedge the novel at "0 of N".
        gemini.Setup(g => g.GenerateReferenceSheet(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Gemini response contained no image data"));

        var novelId = SeedNovel();

        await DriveToCompletion(novelId, _ => { });

        Assert.Equal(ProjectStage.Completed, CurrentStage(novelId));

        using var context = database.NewContext();
        var design = context.DesignRefs.Where(r => r.NovelId == novelId).ToList();
        Assert.NotEmpty(design);
        Assert.All(design, r =>
        {
            Assert.Equal(WorkStatus.Failed, r.ReferenceSheetStatus);
            Assert.NotNull(r.LastError);
            Assert.Null(r.ReferenceImagePath);
        });

        // Panels still render (with whatever reference sheets exist — here, none) and the volume binds.
        var volume = Assert.Single(context.Volumes.Where(v => v.NovelId == novelId));
        Assert.Equal(WorkStatus.Completed, volume.PdfStatus);
        Assert.True(File.Exists(volume.PdfPath!));
    }

    [Fact]
    public async Task RetriesPanelRender_WhenGeminiReturnsNoImage_InsteadOfCrashingTheCycle()
    {
        // A panel render that comes back without an image raises a non-HTTP exception too; it must be
        // scheduled for retry rather than thrown out of ProcessNextAsync.
        var panelCalls = 0;
        gemini.Setup(g => g.GeneratePanel(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                panelCalls++;
                return panelCalls == 1
                    ? Task.FromException<byte[]>(new InvalidOperationException("Gemini response contained no image data"))
                    : Task.FromResult(SampleImages.Png());
            });

        var novelId = SeedNovel();

        await DriveToCompletion(novelId, _ => { });

        Assert.Equal(ProjectStage.Completed, CurrentStage(novelId));
        Assert.True(panelCalls >= 2, "The no-image first panel render should have been retried.");
    }

    [Fact]
    public async Task RecoversFromTransientNetworkDrop_DuringStoryboarding_InsteadOfFailingTheCycle()
    {
        // A socket/IO drop ("Socket closed" is an IOException, not an HttpRequestException) during a
        // Claude call must be retried with backoff — not escape ProcessNextAsync and fail the cycle.
        var storyboardCalls = 0;
        claude.Setup(c => c.Storyboard(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                storyboardCalls++;
                return storyboardCalls == 1
                    ? Task.FromException<StoryboardSegment>(new IOException("Socket closed"))
                    : Task.FromResult(new StoryboardSegment(
                        [new VolumeDraft(1, "Volume One",
                            [new PageDraft(1, [new PanelDraft("Hero stands before the Castle", "At last.", null, ["Hero", "Castle"])])])],
                        "Hero reached the castle."));
            });

        var novelId = SeedNovel();

        await DriveToCompletion(novelId, _ => { });

        Assert.Equal(ProjectStage.Completed, CurrentStage(novelId));
        Assert.True(storyboardCalls >= 2, "The socket-dropped first storyboard call should have been retried.");
    }

    [Fact]
    public async Task StoryboardingFailure_StopsCallingClaudeAfterMaxAttempts()
    {
        // A storyboard call that keeps failing must not retry — and re-bill — forever. After MaxAttempts
        // the novel stalls (Idle) with its error, and Claude is not called again until a manual retry.
        var storyboardCalls = 0;
        claude.Setup(c => c.Storyboard(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                storyboardCalls++;
                return Task.FromException<StoryboardSegment>(new IOException("Socket closed"));
            });

        var novelId = SeedNovel();

        var stalled = false;
        for (int i = 0; i < 400 && !stalled; i++)
        {
            var result = await NewProcessor().ProcessNextAsync(CancellationToken.None);
            if (result.Outcome == ProcessOutcome.Waiting)
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
            else if (result.Outcome == ProcessOutcome.Idle && CurrentStage(novelId) == ProjectStage.Storyboarding)
                stalled = true;
        }

        Assert.True(stalled, "The novel should have stalled once storyboarding exhausted its attempts.");
        Assert.Equal(6, storyboardCalls); // MaxAttempts — never re-called past the cap.

        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == novelId);
        Assert.Equal(ProjectStage.Storyboarding, novel.Stage);
        Assert.Equal(0, novel.StoryboardCursor);
        Assert.Equal(6, novel.StoryboardAttempts);
        Assert.False(string.IsNullOrEmpty(novel.LastError));
    }

    // Drives the pipeline to a terminal stage, invoking inspect() with the current stage before each
    // cycle so a test can disrupt on-disk state mid-run. Fresh processor per cycle proves resume.
    private async Task DriveToCompletion(Guid novelId, Action<ProjectStage> inspect)
    {
        for (int i = 0; i < 400; i++)
        {
            var stage = CurrentStage(novelId);
            if (stage is ProjectStage.Completed or ProjectStage.Failed)
                return;

            inspect(stage);

            var result = await NewProcessor().ProcessNextAsync(CancellationToken.None);
            stage = CurrentStage(novelId);
            if (stage is ProjectStage.Completed or ProjectStage.Failed)
                return;

            if (result.Outcome == ProcessOutcome.Waiting)
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
            else if (result.Outcome == ProcessOutcome.Idle)
                Assert.Fail($"Pipeline stalled in stage {stage} (last error surfaced via DB).");
        }

        Assert.Fail("Pipeline did not reach a terminal stage within the iteration budget.");
    }
}
