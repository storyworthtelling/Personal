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
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string chapterLabel, IReadOnlyList<string> _, CancellationToken _) =>
            {
                chapterAnalysisCalls++;
                return chapterAnalysisCalls == 1
                    ? Task.FromException<ChapterNotes>(new RateLimitException("429 from test"))
                    : Task.FromResult(new ChapterNotes(
                        chapterLabel,
                        ["Hero"], ["Hero walks to the castle"], ["Hero resolves to enter"], [], [], [], ["Vivid setting"], [],
                        ["\"He felt the cold.\" — cut the filter"], ["Filter words recur"], ["Hero sounds like Villain"],
                        ["The hero knocks | opens the confrontation | earns its place"]));
            });

        claude.Setup(c => c.ConsolidateAnalysis(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ChapterNotes>>(), It.IsAny<DesignBible>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookAnalysisResult(
                "A hero reaches a castle.", "The hero journeys to the castle and knocks.",
                [new CharacterProfile("Hero", "protagonist", "Grows from timid to resolute.", ["approaches: Castle"])],
                ["perseverance"],
                [new Finding("Chapter 1", "How did the gate open?")],
                [new Finding("Chapter 1", "Eye colour shifts")],
                [new Finding("Chapter 1", "Castle geography")],
                [new Finding("Chapter 1", "Slow open")],
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
