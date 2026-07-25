using Microsoft.Extensions.Options;
using Moq;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Imaging;
using Storyboarder.Domain.Model;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.Persistence;
using Storyboarder.Infrastructure.Services;
using Storyboarder.Tests.Support;
using Xunit;

namespace Storyboarder.Tests.Services;

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

    private Guid SeedNovel(ProjectStage stage, Action<StoryboarderDbContextSeed>? seed = null)
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
        seed?.Invoke(new StoryboarderDbContextSeed(context, novel.Id));
        context.SaveChanges();
        return novel.Id;
    }

    // Tiny helper so a test can attach related rows to the novel it just seeded.
    private sealed record StoryboarderDbContextSeed(StoryboarderDbContext Context, Guid NovelId);

    private static Chunk NewChunk(Guid novelId, int ordinal) =>
        new() { NovelId = novelId, Ordinal = ordinal, Text = $"chunk {ordinal}" };

    [Fact]
    public async Task Storyboard_progress_tracks_the_cursor_against_the_chunk_count()
    {
        var id = SeedNovel(ProjectStage.Storyboarding, seed =>
        {
            for (var i = 0; i < 5; i++)
                seed.Context.Chunks.Add(NewChunk(seed.NovelId, i));
            var novel = seed.Context.Novels.Local.First();
            novel.StoryboardCursor = 2;
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(2, status!.Storyboard.Done);
        Assert.Equal(5, status.Storyboard.Total);
    }

    [Fact]
    public async Task Storyboard_progress_is_capped_at_the_chunk_count()
    {
        // The cursor lands on chunkCount the moment storyboarding finishes; it must never read past 100%.
        var id = SeedNovel(ProjectStage.Storyboarding, seed =>
        {
            for (var i = 0; i < 3; i++)
                seed.Context.Chunks.Add(NewChunk(seed.NovelId, i));
            seed.Context.Novels.Local.First().StoryboardCursor = 3;
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.Equal(3, status!.Storyboard.Done);
        Assert.Equal(3, status.Storyboard.Total);
    }

    [Fact]
    public async Task AnalysisNotes_progress_counts_completed_chapter_analyses()
    {
        var id = SeedNovel(ProjectStage.ComposingAnalysis, seed =>
        {
            for (var i = 0; i < 4; i++)
                seed.Context.ChunkAnalyses.Add(new ChunkAnalysis
                {
                    NovelId = seed.NovelId,
                    Ordinal = i,
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
    public async Task NeedsAttention_true_when_storyboard_retries_are_exhausted()
    {
        var id = SeedNovel(ProjectStage.Storyboarding, seed =>
        {
            for (var i = 0; i < 5; i++)
                seed.Context.Chunks.Add(NewChunk(seed.NovelId, i));
            var novel = seed.Context.Novels.Local.First();
            novel.StoryboardCursor = 2;
            novel.LastError = "Software caused connection abort";
            // At the attempt cap the storyboard window has given up; the worker won't advance on its
            // own, so this genuinely needs the user to hit Retry.
            novel.StoryboardAttempts = new RateLimitOptions().MaxAttempts;
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.True(status!.NeedsAttention);
        Assert.Equal("Software caused connection abort", status.LastError);
    }

    [Fact]
    public async Task NeedsAttention_false_while_a_transient_error_is_still_retrying()
    {
        var id = SeedNovel(ProjectStage.Storyboarding, seed =>
        {
            for (var i = 0; i < 5; i++)
                seed.Context.Chunks.Add(NewChunk(seed.NovelId, i));
            var novel = seed.Context.Novels.Local.First();
            novel.StoryboardCursor = 2;
            novel.LastError = "Software caused connection abort";
            // Below the cap: the worker will retry this transient error on its own, so it must not
            // raise the "needs attention" alarm.
            novel.StoryboardAttempts = 3;
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.False(status!.NeedsAttention);
    }

    [Fact]
    public async Task FailedVolumeBinding_surfaces_as_needsAttention_with_the_error_and_a_failed_volume()
    {
        var id = SeedNovel(ProjectStage.AssemblingVolumes, seed =>
        {
            seed.Context.Volumes.Add(new Volume
            {
                NovelId = seed.NovelId, Number = 1, Title = "V1",
                PdfStatus = WorkStatus.Completed
            });
            seed.Context.Volumes.Add(new Volume
            {
                NovelId = seed.NovelId, Number = 2, Title = "V2",
                PdfStatus = WorkStatus.Failed, LastError = "binding boom"
            });
        });

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.NotNull(status);
        // A failed binding blocks the book (and the review report) until the user redoes it, so it must
        // be surfaced — with the volume's error and a per-volume Failed flag — rather than silently stuck.
        Assert.True(status!.NeedsAttention);
        Assert.Equal("binding boom", status.LastError);
        Assert.False(status.VolumeList.Single(v => v.Number == 1).Failed);
        Assert.True(status.VolumeList.Single(v => v.Number == 2).Failed);
    }

    [Fact]
    public async Task RunEstimate_isSurfacedEarly_andDropsOnceRenderingStarts()
    {
        // Worth seeing while abandoning the run is still nearly free. Once panels are rendering, the real
        // counts are better information than a projection, so it goes away.
        var early = SeedNovel(ProjectStage.BuildingDesignBible, seed =>
            seed.Context.SourceTexts.Add(new SourceText
            {
                NovelId = seed.NovelId, FullText = "x", CharacterCount = 432_000
            }));

        var status = await NewService().GetStatusAsync(early, CancellationToken.None);

        Assert.NotNull(status!.Estimate);
        Assert.True(status.Estimate!.Panels > 0);
        // A full-length novel is hours against the raised daily image cap (it was weeks at the old 100/day
        // ceiling) — still a multi-hour projection worth surfacing while abandoning the run is nearly free,
        // and still governed by image generation.
        Assert.True(status.Estimate.Duration > TimeSpan.FromHours(1),
            $"expected a multi-hour projection dominated by the image cap, got {status.Estimate.Duration}");
        Assert.Equal("image generation", status.Estimate.LimitingFactor);

        var rendering = SeedNovel(ProjectStage.RenderingImages, seed =>
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

        Assert.Equal(new PhaseProgress(0, 0), status!.Storyboard);
        Assert.Equal(new PhaseProgress(0, 0), status.AnalysisNotes);
    }
}
