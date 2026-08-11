using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PdfSharp.Pdf.IO;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Model;
using Storyboarder.Domain.Pipeline;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.External;
using Storyboarder.Infrastructure.Imaging;
using Storyboarder.Infrastructure.Pipeline;
using Storyboarder.Tests.Support;
using Xunit;

namespace Storyboarder.Tests.Pipeline;

// End-to-end coverage of the Canonizer review pipeline: ingest → design bible (+ reference sheets) →
// per-scene images (none until scene detection is wired) → the map/reduce revision report → archive.
// External services are faked; the report PDF is produced on disk and opens as a valid PDF.
public class ReviewProcessorTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "canonizer-e2e", Guid.NewGuid().ToString("N"));

    private readonly Mock<IClaudeAnalysisClient> claude = new();
    private readonly Mock<IGeminiImageClient> gemini = new();
    private int chapterAnalysisCalls;

    public ReviewProcessorTests()
    {
        claude.Setup(c => c.ExtractEntities(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesignBibleFragment(
                [new EntityMention("Hero", "a brave knight in silver armor")],
                [new EntityMention("Castle", "a grey stone fortress")]));

        claude.Setup(c => c.DeriveArtStyle(It.IsAny<string>(), It.IsAny<DesignBible>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ink-wash watercolor, muted palette");

        // The first chapter-analysis call is rate-limited; the worker must reschedule and succeed on retry.
        claude.Setup(c => c.AnalyzeChapter(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string chapterLabel, IReadOnlyList<string> _, string _, CancellationToken _) =>
            {
                chapterAnalysisCalls++;
                return chapterAnalysisCalls == 1
                    ? Task.FromException<ChapterNotes>(new RateLimitException("429 from test"))
                    : Task.FromResult(new ChapterNotes(
                        chapterLabel,
                        ["Hero"], ["Hero walks to the castle"], ["Hero resolves to enter"], [], [], [], ["Vivid setting"], [],
                        ["\"He felt the cold.\" — cut the filter"], ["Filter words recur"], ["Hero sounds like Villain"],
                        ["The hero knocks | opens the confrontation | earns its place"])
                    {
                        RunningState = $"Story so far through {chapterLabel}: the hero reached the castle."
                    });
            });

        claude.Setup(c => c.ConsolidateAnalysis(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookAnalysisResult(
                "A hero reaches a castle.", "The hero journeys to the castle and knocks.",
                [new CharacterProfile("Hero", "protagonist", "Grows from timid to resolute.", ["approaches: Castle"])],
                ["perseverance"],
                [new ReportFinding("Chapter 1", "How did the gate open?")],
                [new ReportFinding("Chapter 1", "Eye colour shifts")],
                [new ReportFinding("Chapter 1", "Castle geography")],
                [new ReportFinding("Chapter 1", "Slow open")],
                [new Review("developmental editor", "Promising but uneven.", ["Voice"], ["Pacing"], "Tighten the opening.")],
                [new RevisionItem("High", "Chapter 1", "Sagging middle", "Cut a subplot.")],
                ["Comparable Title"], "Upmarket fantasy for adult readers.",
                // The craft sections arrive from ConsolidateCraft and are folded in by the processor.
                [], [], [], []));

        claude.Setup(c => c.ConsolidateCraft(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CraftReport(
                [new LineEdit("Chapter 1", "He felt the cold.", "The cold came.", "Filtered.")],
                [new ProseNote("Chapter 1", "Filter words", "\"he felt\"", "Cut the filter.")],
                [new DialogueNote("Chapter 1", "Hero", "On the nose.", "Trust the subtext.")],
                [new SceneBeat("Chapter 1", "The hero knocks.", "Opens the confrontation.", "Earns its place.")]));

        claude.Setup(c => c.AssessVoiceConsistency(
                It.IsAny<IReadOnlyList<ChapterSample>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VoiceConsistency.Empty);

        // Default reconciliation: treat every current finding as new (no prior-thread match). Tests that care
        // about carry/regress set their own reconciliation.
        claude.Setup(c => c.ReconcileFindings(
                It.IsAny<IReadOnlyList<PriorFinding>>(), It.IsAny<IReadOnlyList<CandidateFinding>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<PriorFinding> _, IReadOnlyList<CandidateFinding> candidates, CancellationToken _) =>
                Task.FromResult(new FindingReconciliation(candidates.Select(c => new FindingMatch(c.Index, null)).ToList())));

        // Default expansion read: a small additive suggestion + a genre range, so the folded report exercises
        // the "Scenes to add"/"Show vs tell"/length path.
        claude.Setup(c => c.ProposeExpansion(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<ChapterNotes> _, string _, int wordCount, string _, CancellationToken _) =>
                Task.FromResult(new ExpansionReport(
                    [new SceneAddition(AdditionKind.CharacterDeepening, "Chapter 1", "the reader never sees her grieve", "the loss lands flat", "Should")],
                    [new ShowTellNote("Chapter 1", "Years passed.", ShowTellDirection.Dramatize, "this leap could be a scene")],
                    new LengthAssessment(wordCount, 90_000, 100_000, LengthGap.Describe(wordCount, 90_000, 100_000)))));

        claude.Setup(c => c.DescribeSceneImage(
                It.IsAny<string>(), It.IsAny<DesignBible>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("A knight stands before a grey castle gate at dusk.");

        gemini.Setup(g => g.GenerateReferenceSheet(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => SampleImages.Png());

        gemini.Setup(g => g.GenerateSceneImage(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
                It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => SampleImages.Png());
    }

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }

    private ReviewProcessor NewProcessor() => new(
        database,
        claude.Object,
        gemini.Object,
        new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot })),
        new AnalysisPdfComposer(new PdfVolumeComposer(), new AnalysisDocumentRenderer()),
        clock,
        Options.Create(new RateLimitOptions
        {
            ClaudeRequestsPerMinute = 1000, ClaudeRequestsPerDay = 100000,
            GeminiRequestsPerMinute = 1000, GeminiRequestsPerDay = 100000,
            BaseDelaySeconds = 4, MaxDelaySeconds = 60, MaxAttempts = 6
        }),
        Options.Create(new ArchiveOptions()),
        NullLogger<ReviewProcessor>.Instance);

    private Guid SeedManuscript(string? fullText = null)
    {
        var text = fullText ?? "A short tale. The hero walked to the castle and knocked on the gate.";
        var novel = new Novel
        {
            Title = "Quest", GoogleDocId = "doc",
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        novel.ProjectId = novel.Id;
        novel.Source = new SourceText { NovelId = novel.Id, FullText = text, CharacterCount = text.Length };

        using var context = database.NewContext();
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    // Drains the pipeline the way the worker does, advancing the clock past any backoff wait so a
    // rescheduled unit becomes due. Bails out after a generous cycle cap so a stuck test fails fast.
    private async Task DriveToCompletion(ReviewProcessor processor)
    {
        for (var cycle = 0; cycle < 200; cycle++)
        {
            var result = await processor.ProcessNextAsync(CancellationToken.None);
            if (result.Outcome == ProcessOutcome.Idle)
            {
                using var context = database.NewContext();
                if (context.Novels.All(n => n.Stage == ProjectStage.Completed || n.Stage == ProjectStage.Failed))
                    return;
            }
            if (result.Outcome == ProcessOutcome.Waiting)
                clock.Advance(result.Wait + TimeSpan.FromSeconds(1));
        }

        Assert.Fail("The pipeline did not settle within the cycle budget.");
    }

    [Fact]
    public async Task Review_ProducesReportPdf_AndCompletes()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == id);
        Assert.Equal(ProjectStage.Completed, novel.Stage);

        var analysis = context.BookAnalyses.Single(a => a.NovelId == id);
        Assert.Equal(WorkStatus.Completed, analysis.PdfStatus);
        Assert.NotNull(analysis.PdfPath);
        Assert.True(File.Exists(analysis.PdfPath));

        // The rendered report opens as a valid PDF.
        using var document = PdfReader.Open(analysis.PdfPath!, PdfDocumentOpenMode.ReadOnly);
        Assert.True(document.PageCount > 0);
    }

    [Fact]
    public async Task RateLimitedChapterAnalysis_RetriesAndSucceeds()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        // The first AnalyzeChapter threw a rate limit; the pipeline must have rescheduled and retried.
        Assert.True(chapterAnalysisCalls >= 2);

        using var context = database.NewContext();
        Assert.All(
            context.ChunkAnalyses.Where(a => a.NovelId == id).ToList(),
            a => Assert.Equal(WorkStatus.Completed, a.Status));
    }

    [Fact]
    public async Task Scenes_AreDetected_AndGetEvidenceImages()
    {
        var id = SeedManuscript("The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.");
        await DriveToCompletion(NewProcessor());

        using var context = database.NewContext();
        var scenes = context.Scenes.Where(s => s.NovelId == id).OrderBy(s => s.Number).ToList();
        Assert.True(scenes.Count >= 2, "expected the divider to split the manuscript into scenes");
        Assert.All(scenes, s => Assert.Equal(WorkStatus.Completed, s.ImageStatus));
        Assert.All(scenes, s =>
        {
            Assert.NotNull(s.ImagePath);
            Assert.True(File.Exists(s.ImagePath));
        });
    }

    // Seeds a follow-on draft of an existing project the way ProjectService.CreateNextDraftAsync does:
    // same ProjectId, the next DraftNumber, and PreviousDraftId pointing at the draft it was made from.
    private Guid SeedNextDraft(Guid previousId, string text)
    {
        using var context = database.NewContext();
        var previous = context.Novels.Single(n => n.Id == previousId);
        var draft = new Novel
        {
            Title = previous.Title, GoogleDocId = "doc",
            ProjectId = previous.ProjectId, DraftNumber = previous.DraftNumber + 1, PreviousDraftId = previous.Id,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        draft.Source = new SourceText { NovelId = draft.Id, FullText = text, CharacterCount = text.Length };
        context.Novels.Add(draft);
        context.SaveChanges();
        return draft.Id;
    }

    [Fact]
    public async Task SecondDraft_UnchangedText_ReusesEverySceneImage()
    {
        const string text = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.";
        var firstId = SeedManuscript(text);
        await DriveToCompletion(NewProcessor());

        // The second draft re-picks the identical document — every scene should reuse the prior render.
        gemini.Invocations.Clear();
        var secondId = SeedNextDraft(firstId, text);
        await DriveToCompletion(NewProcessor());

        gemini.Verify(g => g.GenerateSceneImage(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
            It.IsAny<double?>(), It.IsAny<CancellationToken>()), Times.Never());

        using var context = database.NewContext();
        var scenes = context.Scenes.Where(s => s.NovelId == secondId).OrderBy(s => s.Number).ToList();
        Assert.True(scenes.Count >= 2);
        Assert.All(scenes, s =>
        {
            Assert.Equal(WorkStatus.Completed, s.ImageStatus);
            Assert.False(s.Changed);
            Assert.NotNull(s.PreviousSceneId);
            Assert.NotNull(s.ImagePath);
            Assert.True(File.Exists(s.ImagePath));
        });
    }

    [Fact]
    public async Task SecondDraft_RerendersOnlyTheChangedScene()
    {
        const string first = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked.";
        // Scene 1 is byte-identical; scene 2 is reworded but shares most content words (a changed match).
        const string second = "The knight rode to the castle.\n\n* * *\n\nAt the gate, he knocked loudly and waited in the cold.";
        var firstId = SeedManuscript(first);
        await DriveToCompletion(NewProcessor());

        gemini.Invocations.Clear();
        var secondId = SeedNextDraft(firstId, second);
        await DriveToCompletion(NewProcessor());

        // Exactly one fresh render — the changed scene. The unchanged scene reused its prior image.
        gemini.Verify(g => g.GenerateSceneImage(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>(),
            It.IsAny<double?>(), It.IsAny<CancellationToken>()), Times.Once());

        using var context = database.NewContext();
        var scenes = context.Scenes.Where(s => s.NovelId == secondId).OrderBy(s => s.Number).ToList();
        var changed = scenes.Where(s => s.Changed).ToList();
        Assert.Single(changed);
        Assert.All(scenes, s => Assert.NotNull(s.PreviousSceneId));   // both matched a prior scene
        Assert.All(scenes, s => Assert.Equal(WorkStatus.Completed, s.ImageStatus));
    }

    [Fact]
    public async Task Map_ThreadsRunningStoryStateIntoContinuitySummary()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == id);
        // The map step carried the model's running state onto the manuscript for the consolidation to use.
        Assert.False(string.IsNullOrWhiteSpace(novel.ContinuitySummary));
        Assert.Contains("the hero reached the castle", novel.ContinuitySummary);
    }

    [Fact]
    public async Task FirstDraft_PersistsFindingsAsRows_AllNew()
    {
        var id = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        using var context = database.NewContext();
        var findings = context.Findings.Where(f => f.NovelId == id).ToList();
        // The faked story-half returns one finding in each of the four categories.
        Assert.Equal(4, findings.Count);
        Assert.All(findings, f =>
        {
            Assert.Equal(FindingStatus.New, f.Status);
            Assert.Equal(1, f.FirstDraftNumber);
            Assert.NotEqual(Guid.Empty, f.ThreadId);
        });
        Assert.Contains(findings, f => f.Category == FindingCategory.PlotHole);
        Assert.Contains(findings, f => f.Category == FindingCategory.Pacing);
    }

    [Fact]
    public async Task SecondDraft_CarriesAMatchedFindingByThreadId()
    {
        var firstId = SeedManuscript();
        await DriveToCompletion(NewProcessor());

        // The plot-hole thread from draft 1 (candidate index 0 in the reconciliation order).
        Guid plotHoleThread;
        using (var context = database.NewContext())
            plotHoleThread = context.Findings.Single(f => f.NovelId == firstId && f.Category == FindingCategory.PlotHole).ThreadId;

        // For draft 2, tell the reconciliation the plot hole is the same issue as draft 1's; the rest are new.
        claude.Setup(c => c.ReconcileFindings(
                It.IsAny<IReadOnlyList<PriorFinding>>(), It.IsAny<IReadOnlyList<CandidateFinding>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<PriorFinding> prior, IReadOnlyList<CandidateFinding> candidates, CancellationToken _) =>
                Task.FromResult(new FindingReconciliation(
                    candidates.Select(c => new FindingMatch(c.Index, c.Index == 0 ? plotHoleThread : (Guid?)null)).ToList())));

        var secondId = SeedNextDraft(firstId, "A short tale. The hero walked to the castle and knocked on the gate.");
        await DriveToCompletion(NewProcessor());

        using var check = database.NewContext();
        var carried = check.Findings.Single(f => f.NovelId == secondId && f.Category == FindingCategory.PlotHole);
        Assert.Equal(plotHoleThread, carried.ThreadId);   // same identity across drafts
        Assert.Equal(FindingStatus.Carried, carried.Status);
        Assert.Equal(1, carried.FirstDraftNumber);          // open since draft 1
        // The other three findings had no match, so they are new threads this draft.
        Assert.Equal(3, check.Findings.Count(f => f.NovelId == secondId && f.Status == FindingStatus.New));
    }

    [Fact]
    public async Task DesignBible_RendersSheetsForRecurringEntities()
    {
        // Two chunks so an entity seen in both is "recurring" and earns a reference sheet.
        var id = SeedManuscript(string.Join("\n\n", Enumerable.Repeat("The Hero rode toward the Castle.", 4000)));
        await DriveToCompletion(NewProcessor());

        using var context = database.NewContext();
        var sheets = context.DesignRefs.Where(r => r.NovelId == id && r.NeedsSheet).ToList();
        Assert.NotEmpty(sheets);
        Assert.All(sheets, r => Assert.Equal(WorkStatus.Completed, r.ReferenceSheetStatus));
    }
}
