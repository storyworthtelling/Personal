using Microsoft.Extensions.Options;
using Moq;
using Folio.Domain.External;
using Folio.Domain.Imaging;
using Folio.Domain.Model;
using Folio.Domain.Reading;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Persistence;
using Folio.Infrastructure.Services;
using Folio.Tests.Support;
using Xunit;

namespace Folio.Tests.Services;

// Covers the progress read model the library card renders: every long-running stage must report a
// Done/Total so the user can see what the worker is on and how much of it is left.
public class NovelStatusTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly Mock<IGoogleDocsClient> docs = new();
    private readonly Mock<IImageStore> imageStore = new();

    public void Dispose() => database.Dispose();

    private ProjectService NewService() =>
        new(database, docs.Object, imageStore.Object, clock, Options.Create(new RateLimitOptions()));

    private Guid SeedNovel(ProjectStage stage, Action<FolioDbContextSeed>? seed = null)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Quest", GoogleDocId = "doc", Stage = stage,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        context.Novels.Add(novel);
        seed?.Invoke(new FolioDbContextSeed(context, novel.Id));
        context.SaveChanges();
        return novel.Id;
    }

    // Tiny helper so a test can attach related rows to the novel it just seeded.
    private sealed record FolioDbContextSeed(FolioDbContext Context, Guid NovelId);

    [Fact]
    public async Task SceneImages_progress_counts_rendered_images()
    {
        var id = SeedNovel(ProjectStage.RenderingSceneImages, seed =>
        {
            for (var i = 0; i < 4; i++)
                seed.Context.Scenes.Add(new Scene
                {
                    NovelId = seed.NovelId, Number = i + 1,
                    ImageStatus = i < 3 ? WorkStatus.Completed : WorkStatus.Pending
                });
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(3, status!.SceneImages.Done);
        Assert.Equal(4, status.SceneImages.Total);
    }

    [Fact]
    public async Task AnalysisNotes_progress_counts_completed_chapter_analyses()
    {
        var id = SeedNovel(ProjectStage.ComposingAnalysis, seed =>
        {
            for (var i = 0; i < 4; i++)
                seed.Context.ChunkAnalyses.Add(new ChunkAnalysis
                {
                    NovelId = seed.NovelId, Ordinal = i,
                    Status = i < 3 ? WorkStatus.Completed : WorkStatus.Pending
                });
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.Equal(3, status!.AnalysisNotes.Done);
        Assert.Equal(4, status.AnalysisNotes.Total);
    }

    [Fact]
    public async Task ReferenceSheets_progress_counts_rendered_sheets()
    {
        var id = SeedNovel(ProjectStage.BuildingDesignBible, seed =>
        {
            seed.Context.DesignRefs.Add(new DesignRef
            {
                NovelId = seed.NovelId, Kind = ReferenceKind.Character, Name = "Hero",
                ReferenceSheetStatus = WorkStatus.Completed
            });
            seed.Context.DesignRefs.Add(new DesignRef
            {
                NovelId = seed.NovelId, Kind = ReferenceKind.Setting, Name = "Castle",
                ReferenceSheetStatus = WorkStatus.Pending
            });
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.Equal(1, status!.ReferenceSheets.Done);
        Assert.Equal(2, status.ReferenceSheets.Total);
    }

    [Fact]
    public async Task NeedsAttention_true_when_a_step_has_exhausted_its_retries()
    {
        var id = SeedNovel(ProjectStage.ComposingAnalysis, seed =>
        {
            var novel = seed.Context.Novels.Local.First();
            novel.LastError = "Software caused connection abort";
            // At the attempt cap the once-per-manuscript step has given up; the worker won't advance on
            // its own, so this genuinely needs the user to hit Retry.
            novel.StageAttempts = new RateLimitOptions().MaxAttempts;
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.True(status!.NeedsAttention);
        Assert.Equal("Software caused connection abort", status.LastError);
    }

    [Fact]
    public async Task NeedsAttention_false_while_a_transient_error_is_still_retrying()
    {
        var id = SeedNovel(ProjectStage.ComposingAnalysis, seed =>
        {
            var novel = seed.Context.Novels.Local.First();
            novel.LastError = "Software caused connection abort";
            // Below the cap: the worker will retry this transient error on its own.
            novel.StageAttempts = 3;
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.False(status!.NeedsAttention);
    }

    [Fact]
    public async Task RunEstimate_isSurfacedEarly_andDropsOnceRenderingStarts()
    {
        // Worth seeing while abandoning the run is still nearly free. Once scene images are rendering, the
        // real counts are better information than a projection, so it goes away.
        var early = SeedNovel(ProjectStage.BuildingDesignBible, seed =>
            seed.Context.SourceTexts.Add(new SourceText
            {
                NovelId = seed.NovelId, FullText = "x", CharacterCount = 432_000
            }));

        var status = await NewService().GetStatusAsync(early, CancellationToken.None);

        Assert.NotNull(status!.Estimate);
        Assert.True(status.Estimate!.Scenes > 0);
        Assert.True(status.Estimate.ClaudeCalls > 0);

        var rendering = SeedNovel(ProjectStage.RenderingSceneImages, seed =>
            seed.Context.SourceTexts.Add(new SourceText
            {
                NovelId = seed.NovelId, FullText = "x", CharacterCount = 432_000
            }));

        Assert.Null((await NewService().GetStatusAsync(rendering, CancellationToken.None))!.Estimate);
    }

    [Fact]
    public async Task RunEstimate_isNull_beforeTheManuscriptIsIngested()
    {
        var id = SeedNovel(ProjectStage.Created);

        Assert.Null((await NewService().GetStatusAsync(id, CancellationToken.None))!.Estimate);
    }

    [Fact]
    public async Task Progress_phases_are_zero_when_no_work_has_been_seeded()
    {
        var id = SeedNovel(ProjectStage.Created);

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.Equal(new PhaseProgress(0, 0), status!.SceneImages);
        Assert.Equal(new PhaseProgress(0, 0), status.AnalysisNotes);
    }

    // ---- Which read is happening ----------------------------------------------------------------------

    private Guid SeedReading(int readsDone, Action<Novel>? shape = null)
    {
        return SeedNovel(ProjectStage.ComposingAnalysis, seed =>
        {
            var novel = seed.Context.Novels.Local.First();
            shape?.Invoke(novel);
            for (var i = 0; i < 2; i++)
                seed.Context.ChunkAnalyses.Add(new ChunkAnalysis
                {
                    NovelId = seed.NovelId, Ordinal = i, Status = WorkStatus.Completed
                });
            seed.Context.BookAnalyses.Add(new BookAnalysis
            {
                NovelId = seed.NovelId, ConsolidatedJson = "{}", CraftJson = "{}", VoiceJson = "{}",
                ExpansionJson = "{}", FindingsReconciled = true
            });
            for (var i = 0; i < readsDone; i++)
                seed.Context.PassReads.Add(new PassRead
                {
                    NovelId = seed.NovelId, PassId = ReadingPlan.Passes[i].Id, Json = "{}"
                });
        });
    }

    [Fact]
    public async Task Reads_progress_counts_the_passes_that_have_finished()
    {
        var id = SeedReading(readsDone: 3);

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.Equal(3, status!.Reads!.Done);
        Assert.Equal(ReadingPlan.Passes.Count, status.Reads.Total);
    }

    [Fact]
    public async Task The_step_names_the_next_read_and_its_place_in_the_plan()
    {
        var id = SeedReading(readsDone: 3);

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.Equal(
            $"Reading the whole book through — read 4 of {ReadingPlan.Passes.Count}: {ReadingPlan.Passes[3].Name}",
            status!.Step);
    }

    [Fact]
    public async Task A_pass_that_failed_is_not_counted_as_read()
    {
        var id = SeedNovel(ProjectStage.ComposingAnalysis, seed =>
        {
            seed.Context.ChunkAnalyses.Add(new ChunkAnalysis
            {
                NovelId = seed.NovelId, Ordinal = 0, Status = WorkStatus.Completed
            });
            seed.Context.BookAnalyses.Add(new BookAnalysis
            {
                NovelId = seed.NovelId, ConsolidatedJson = "{}", CraftJson = "{}", VoiceJson = "{}",
                ExpansionJson = "{}", FindingsReconciled = true
            });
            seed.Context.PassReads.Add(new PassRead
            {
                NovelId = seed.NovelId, PassId = ReadingPlan.Passes[0].Id, Json = null, Attempts = 5,
                LastError = "truncated"
            });
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.Equal(0, status!.Reads!.Done);
        Assert.Contains($"read 1 of {ReadingPlan.Passes.Count}: {ReadingPlan.Passes[0].Name}", status.Step);
    }

    [Fact]
    public async Task Before_the_reads_the_step_is_whichever_reduction_is_next()
    {
        var id = SeedNovel(ProjectStage.ComposingAnalysis, seed =>
        {
            seed.Context.ChunkAnalyses.Add(new ChunkAnalysis
            {
                NovelId = seed.NovelId, Ordinal = 0, Status = WorkStatus.Completed
            });
            seed.Context.BookAnalyses.Add(new BookAnalysis
            {
                NovelId = seed.NovelId, ConsolidatedJson = "{}"   // the story report is done; craft is next
            });
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.Equal("Gathering the line edits, prose and dialogue notes", status!.Step);
    }

    [Fact]
    public async Task There_is_no_step_outside_the_report_stage()
    {
        var id = SeedNovel(ProjectStage.RenderingSceneImages);

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.Null(status!.Step);
        Assert.Null(status.Reads);
    }

    [Fact]
    public async Task The_status_carries_the_attempts_used_and_allowed()
    {
        var id = SeedReading(readsDone: 1, novel =>
        {
            novel.LastError = "Reading pass 'x': IOException: Software caused connection abort";
            novel.StageAttempts = 2;
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.Equal(2, status!.Attempts);
        Assert.Equal(new RateLimitOptions().MaxAttempts, status.MaxAttempts);
    }
}
