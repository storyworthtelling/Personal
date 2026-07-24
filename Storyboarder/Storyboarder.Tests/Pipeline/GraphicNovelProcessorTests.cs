using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PdfSharp.Pdf.IO;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Imaging;
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

    // When set, NewProcessor binds volumes through this composer instead of a plain one, so a test can
    // make PDF assembly throw. It's a field (not a NewProcessor arg) so the fresh processor DriveToCompletion
    // builds each cycle keeps using it.
    private IVolumePdfComposer? volumeComposerOverride;

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
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<CancellationToken>()))
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

    private GraphicNovelProcessor NewProcessor(int maxPanelsPerPage = 6) => new(
        database,
        claude.Object,
        gemini.Object,
        new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot })),
        new SkiaPageComposer(Options.Create(new PageLayoutOptions())),
        volumeComposerOverride ?? new PdfVolumeComposer(),
        new AnalysisPdfComposer(new PdfVolumeComposer(), new AnalysisDocumentRenderer()),
        clock,
        Options.Create(new RateLimitOptions
        {
            ClaudeRequestsPerMinute = 1000, ClaudeRequestsPerDay = 100000,
            GeminiRequestsPerMinute = 1000, GeminiRequestsPerDay = 100000,
            BaseDelaySeconds = 4, MaxDelaySeconds = 60, MaxAttempts = 6
        }),
        Options.Create(new PageLayoutOptions { MaxPanelsPerPage = maxPanelsPerPage }),
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
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<CancellationToken>()))
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
    public async Task QuotaExhaustedPanelRender_DefersLong_WithoutFalseResumeTime_ThenRecovers()
    {
        // Depleted Gemini credits surface as a QuotaExhausted rate limit. Unlike a per-minute throttle,
        // that clears only when the user tops up billing, so the panel must be deferred a long, flat
        // interval (not the short base backoff) and the novel must NOT advertise a resume time it can't
        // keep. The panel stays retryable — never Failed — so it resumes once credits return.
        var panelCalls = 0;
        gemini.Setup(g => g.GeneratePanel(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                panelCalls++;
                return panelCalls == 1
                    ? Task.FromException<byte[]>(new RateLimitException(
                        "Gemini quota exhausted: 429: your prepayment credits are depleted",
                        retryAfter: null, RateLimitKind.QuotaExhausted))
                    : Task.FromResult(SampleImages.Png());
            });

        var novelId = SeedNovel();

        // Drive until the quota-exhausted panel has been attempted once and deferred.
        var deferredSeen = false;
        for (int i = 0; i < 400 && !deferredSeen; i++)
        {
            var result = await NewProcessor().ProcessNextAsync(CancellationToken.None);

            using (var ctx = database.NewContext())
            {
                var panel = ctx.Panels.FirstOrDefault(p => p.Status == WorkStatus.NeedsRetry);
                if (panel is not null)
                {
                    // Deferred by ~MaxDelaySeconds (60 in the test config), not the 4s base delay.
                    Assert.True(panel.NextAttemptAtUtc >= clock.UtcNow + TimeSpan.FromSeconds(59),
                        $"expected a long quota deferral, got {panel.NextAttemptAtUtc - clock.UtcNow}");
                    // No false "resumes shortly" — we can't predict when billing is topped up.
                    var novel = ctx.Novels.Single(n => n.Id == novelId);
                    Assert.Null(novel.RateLimitedUntilUtc);
                    deferredSeen = true;
                }
            }

            if (!deferredSeen && result.Outcome == ProcessOutcome.Waiting)
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
        }

        Assert.True(deferredSeen, "The quota-exhausted panel should have been deferred, not failed.");

        // Once credits return the deferred panel retries and the book finishes.
        await DriveToCompletion(novelId, _ => { });
        Assert.Equal(ProjectStage.Completed, CurrentStage(novelId));
        Assert.True(panelCalls >= 2, "The quota-exhausted panel should have been retried after the deferral.");
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

    [Fact]
    public async Task AssemblesMultiPageVolume_OnePagePerCycle_ResumingFromTheCursor()
    {
        // A larger volume must not be assembled in one monolithic, memory-heavy pass (which stalled the
        // worker on big books). It is built one page per cycle, resuming from PdfPageCursor — proven here
        // by a fresh processor each cycle and by the cursor advancing exactly one page at a time.
        claude.Setup(c => c.Storyboard(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoryboardSegment(
                [new VolumeDraft(1, "Volume One",
                    [
                        new PageDraft(1, [new PanelDraft("Hero approaches", "One.", null, ["Hero", "Castle"])]),
                        new PageDraft(2, [new PanelDraft("Hero knocks", "Two.", null, ["Hero", "Castle"])]),
                        new PageDraft(3, [new PanelDraft("The gate opens", "Three.", null, ["Hero", "Castle"])]),
                    ])],
                "Hero reached the castle."));

        var novelId = SeedNovel();

        var observedCursors = new List<int>();
        await DriveToCompletion(novelId, stage =>
        {
            if (stage != ProjectStage.AssemblingVolumes)
                return;
            using var context = database.NewContext();
            var pending = context.Volumes.FirstOrDefault(v => v.NovelId == novelId && v.PdfStatus == WorkStatus.Pending);
            if (pending is not null)
                observedCursors.Add(pending.PdfPageCursor);
        });

        Assert.Equal(ProjectStage.Completed, CurrentStage(novelId));

        // One append per cycle: the cursor was seen at 0, 1, then 2 before the final page completed it.
        Assert.Equal([0, 1, 2], observedCursors);

        using var ctx = database.NewContext();
        var volume = Assert.Single(ctx.Volumes.Where(v => v.NovelId == novelId));
        Assert.Equal(WorkStatus.Completed, volume.PdfStatus);
        Assert.Equal(3, volume.PdfPageCursor);
        Assert.True(File.Exists(volume.PdfPath!));

        using var stream = new MemoryStream(File.ReadAllBytes(volume.PdfPath!));
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(4, pdf.PageCount); // cover + 3 pages
    }

    [Fact]
    public async Task Storyboard_splits_a_page_over_the_cap_into_several_balanced_pages()
    {
        // Claude returns a single page of 8 panels; with the cap at 6 it must become two physical pages
        // of 4 panels each (balanced, not 6+2) so panels are never squished into a dense grid.
        claude.Setup(c => c.Storyboard(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoryboardSegment(
                [new VolumeDraft(1, "Volume One",
                    [new PageDraft(1, Enumerable.Range(1, 8)
                        .Select(i => new PanelDraft($"Beat {i} in action", null, null, ["Hero"]))
                        .ToList())])],
                "Hero acted."));

        var novelId = SeedNovel();
        await DriveToCompletion(novelId, _ => { });

        Assert.Equal(ProjectStage.Completed, CurrentStage(novelId));

        using var ctx = database.NewContext();
        var volumeIds = ctx.Volumes.Where(v => v.NovelId == novelId).Select(v => v.Id).ToList();
        var pages = ctx.Pages.Where(p => volumeIds.Contains(p.VolumeId)).OrderBy(p => p.PageNumber).ToList();

        // Eight panels at a cap of six became two pages, split 4 + 4, numbered consecutively.
        Assert.Equal(2, pages.Count);
        Assert.Equal([1, 2], pages.Select(p => p.PageNumber).ToList());
        Assert.All(pages, p => Assert.Equal(4, ctx.Panels.Count(pn => pn.PageId == p.Id)));
    }

    [Fact]
    public async Task VolumeBinding_ThatThrowsOnce_RetriesWithBackoff_ThenCompletes()
    {
        // A throw during PDF assembly used to mark the volume Failed permanently, pinning the novel at
        // AssemblingVolumes forever. It must instead retry with backoff and bind on recovery.
        volumeComposerOverride = new FlakyPdfComposer(failTimes: 1);

        var novelId = SeedNovel();
        await DriveToCompletion(novelId, _ => { });

        Assert.Equal(ProjectStage.Completed, CurrentStage(novelId));

        using var ctx = database.NewContext();
        var volume = Assert.Single(ctx.Volumes.Where(v => v.NovelId == novelId));
        Assert.Equal(WorkStatus.Completed, volume.PdfStatus);
        Assert.True(File.Exists(volume.PdfPath!));
        // The one failure was recorded then cleared on the clean bind.
        Assert.Equal(0, volume.Attempts);
        Assert.Null(volume.LastError);
    }

    [Fact]
    public async Task VolumeBinding_ThatKeepsThrowing_FailsAfterMaxAttempts_BlockingAnalysis()
    {
        // A binding that never succeeds exhausts its retry budget and stops (Failed), leaving the novel
        // at AssemblingVolumes with the review report not yet produced — the state a manual retry fixes.
        volumeComposerOverride = new FlakyPdfComposer(failTimes: int.MaxValue);

        var novelId = SeedNovel();

        var failed = false;
        for (int i = 0; i < 400 && !failed; i++)
        {
            var result = await NewProcessor().ProcessNextAsync(CancellationToken.None);
            if (result.Outcome == ProcessOutcome.Waiting)
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));

            using var ctx = database.NewContext();
            if (ctx.Volumes.Any(v => v.NovelId == novelId && v.PdfStatus == WorkStatus.Failed))
                failed = true;
        }

        Assert.True(failed, "The binding should have exhausted its retries and been marked Failed.");

        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == novelId);
        Assert.Equal(ProjectStage.AssemblingVolumes, novel.Stage);
        var volume = Assert.Single(context.Volumes.Where(v => v.NovelId == novelId));
        Assert.Equal(WorkStatus.Failed, volume.PdfStatus);
        Assert.False(string.IsNullOrEmpty(volume.LastError));
        // The gate held: the analysis report was never seeded, let alone rendered.
        Assert.Empty(context.BookAnalyses.Where(a => a.NovelId == novelId));
    }

    // A volume PDF composer that throws on its first `failTimes` AppendPage calls, then delegates to a
    // real composer — so a test can exercise binding retry/failure and still get a valid PDF on recovery.
    private sealed class FlakyPdfComposer(int failTimes) : IVolumePdfComposer
    {
        private readonly PdfVolumeComposer inner = new();
        private int failures;

        public byte[] Compose(IReadOnlyList<byte[]> pageImages, int dpi) => inner.Compose(pageImages, dpi);

        public byte[] AppendPage(byte[]? existingPdf, byte[] pageImage, int dpi)
        {
            if (failures < failTimes)
            {
                failures++;
                throw new InvalidOperationException("simulated binding failure");
            }
            return inner.AppendPage(existingPdf, pageImage, dpi);
        }
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
