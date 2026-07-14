using Microsoft.Extensions.Options;
using Moq;
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
    public async Task RetryAsync_LeavesCancelledProjectsAlone()
    {
        var id = SeedNovel(ProjectStage.Cancelled);

        await NewService().RetryAsync(id, CancellationToken.None);

        using var context = database.NewContext();
        Assert.Equal(ProjectStage.Cancelled, context.Novels.Single(n => n.Id == id).Stage);
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
