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

    private Guid SeedNovel()
    {
        var novel = new Novel { Title = "Quest", GoogleDocId = "doc", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        novel.Source = new SourceText
        {
            NovelId = novel.Id,
            FullText = "A short tale. The hero walked to the castle and knocked on the gate.",
            CharacterCount = 64
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
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(volume.PdfPath!)[..4]));

        Assert.True(chapterAnalysisCalls >= 2, "The rate-limited first chapter analysis should have been retried.");
        var analysis = Assert.Single(context.BookAnalyses.Where(a => a.NovelId == novelId));
        Assert.Equal(WorkStatus.Completed, analysis.PdfStatus);
        Assert.NotNull(analysis.PdfPath);
        Assert.True(File.Exists(analysis.PdfPath));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(analysis.PdfPath!)[..4]));
    }
}
