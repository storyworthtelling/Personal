using Moq;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Imaging;
using Storyboarder.Domain.Model;
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

    private ProjectService NewService() => new(database, docs.Object, imageStore.Object, clock);

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
    public async Task Progress_phases_are_zero_when_no_work_has_been_seeded()
    {
        var id = SeedNovel(ProjectStage.Created);

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.Equal(new PhaseProgress(0, 0), status!.Storyboard);
        Assert.Equal(new PhaseProgress(0, 0), status.AnalysisNotes);
    }
}
