using Microsoft.Extensions.Options;
using Moq;
using Folio.Domain.External;
using Folio.Domain.Model;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Imaging;
using Folio.Infrastructure.Services;
using Folio.Tests.Support;
using Xunit;

namespace Folio.Tests.Shelf;

/// <summary>
/// Putting a book on the shelf is something the owner does.
///
/// <para><b>Finishing a review and putting the book away are two different events</b>, and the
/// frame separates them: the desk is what is being handled and the shelf is what has been dealt
/// with. A book whose review finished while the owner was asleep has not been read by anyone yet —
/// sweeping it onto the shelf on their behalf is the app deciding it is done with.</para>
///
/// <para>So <c>Completed</c> leaves a book on the desk, and it moves when <i>Put on the shelf</i> is
/// chosen. Undo-first, like every other act that takes something away from where it was.</para>
/// </summary>
public class ShelvingTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly Mock<IGoogleDocsClient> docs = new();
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "folio-shelving", Guid.NewGuid().ToString("N"));
    private readonly FileSystemImageStore imageStore;

    public ShelvingTests() =>
        imageStore = new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot }));

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }

    private ProjectService NewService() =>
        new(database, docs.Object, imageStore, clock, Options.Create(new RateLimitOptions()));

    private Guid Seed(ProjectStage stage = ProjectStage.Completed)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Quest", GoogleDocId = "doc", Stage = stage,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow,
        };
        novel.ProjectId = novel.Id;
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    [Fact]
    public async Task AFinishedBookIsNotShelvedByItself()
    {
        var id = Seed();

        var status = await NewService().GetStatusAsync(id, CancellationToken.None);

        Assert.NotNull(status);
        Assert.False(status!.Shelved, "Finishing a review is not the same event as putting the book away.");
    }

    [Fact]
    public async Task PuttingItOnTheShelfShelvesIt()
    {
        var id = Seed();
        var service = NewService();

        await service.ShelveAsync(id, CancellationToken.None);

        Assert.True((await service.GetStatusAsync(id, CancellationToken.None))!.Shelved);
    }

    // Undo-first (§6): the dog-ear offers it back, so taking it off the shelf must be possible and
    // must leave no trace of having happened.
    [Fact]
    public async Task TakingItBackOffTheShelfUndoesItCompletely()
    {
        var id = Seed();
        var service = NewService();

        await service.ShelveAsync(id, CancellationToken.None);
        await service.UnshelveAsync(id, CancellationToken.None);

        Assert.False((await service.GetStatusAsync(id, CancellationToken.None))!.Shelved);
    }

    [Fact]
    public async Task ShelvingTwiceIsNotAnError()
    {
        var id = Seed();
        var service = NewService();

        await service.ShelveAsync(id, CancellationToken.None);
        await service.ShelveAsync(id, CancellationToken.None);

        Assert.True((await service.GetStatusAsync(id, CancellationToken.None))!.Shelved);
    }

    /// <summary>
    /// An unfinished book cannot be put away. The shelf is what has been dealt with; a book still
    /// being read has not been, and shelving it would hide work in progress.
    /// </summary>
    [Fact]
    public async Task AnUnfinishedBookCannotBeShelved()
    {
        var id = Seed(ProjectStage.ComposingAnalysis);

        await NewService().ShelveAsync(id, CancellationToken.None);

        Assert.False((await NewService().GetStatusAsync(id, CancellationToken.None))!.Shelved);
    }

    [Fact]
    public async Task TheShelfListsOnlyWhatHasBeenPutOnIt()
    {
        var shelved = Seed();
        Seed();
        var service = NewService();
        await service.ShelveAsync(shelved, CancellationToken.None);

        var onShelf = await service.ListShelvedAsync(CancellationToken.None);

        Assert.Equal(shelved, Assert.Single(onShelf).Id);
    }

    [Fact]
    public async Task TheDeskListsWhatIsNotOnTheShelf()
    {
        var shelved = Seed();
        var onDesk = Seed();
        var service = NewService();
        await service.ShelveAsync(shelved, CancellationToken.None);

        var desk = await service.ListAsync(CancellationToken.None);

        Assert.Equal(onDesk, Assert.Single(desk).Id);
    }
}
