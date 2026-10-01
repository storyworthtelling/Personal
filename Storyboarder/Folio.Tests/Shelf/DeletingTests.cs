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
/// Deleting a book, undo-first (DESIGN.md §6).
///
/// <para><b>A destructive act here is not a hard delete.</b> Choosing Delete sets
/// <see cref="Novel.DeletedAtUtc"/> at once — the book vanishes from both the Desk's and the
/// Shelf's own listings immediately — and the fold in the corner puts it back exactly as it was.
/// The real, irreversible <see cref="ProjectService.DeleteAsync"/> (rows and rendered files gone for
/// good) only runs once the undo window has closed without being turned; that timing lives on the
/// page, not here, so this tests only the two states a book can be in along the way: soft-deleted
/// (hidden, restorable) and hard-deleted (gone).</para>
/// </summary>
public class DeletingTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly Mock<IGoogleDocsClient> docs = new();
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "folio-deleting", Guid.NewGuid().ToString("N"));
    private readonly FileSystemImageStore imageStore;

    public DeletingTests() =>
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
    public async Task SoftDeletingHidesItFromTheDesk()
    {
        var id = Seed(ProjectStage.Created);
        var service = NewService();

        await service.SoftDeleteAsync(id, CancellationToken.None);

        Assert.Empty(await service.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SoftDeletingHidesItFromTheShelf()
    {
        var id = Seed();
        var service = NewService();
        await service.ShelveAsync(id, CancellationToken.None);

        await service.SoftDeleteAsync(id, CancellationToken.None);

        Assert.Empty(await service.ListShelvedAsync(CancellationToken.None));
    }

    // Undo-first (§6): the fold offers it back, so restoring must leave no trace of having happened.
    [Fact]
    public async Task RestoringUndoesItCompletely()
    {
        var id = Seed(ProjectStage.Created);
        var service = NewService();

        await service.SoftDeleteAsync(id, CancellationToken.None);
        await service.RestoreAsync(id, CancellationToken.None);

        Assert.Equal(id, Assert.Single(await service.ListAsync(CancellationToken.None)).Id);
    }

    [Fact]
    public async Task SoftDeletingTwiceIsNotAnError()
    {
        var id = Seed(ProjectStage.Created);
        var service = NewService();

        await service.SoftDeleteAsync(id, CancellationToken.None);
        await service.SoftDeleteAsync(id, CancellationToken.None);

        Assert.Empty(await service.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RestoringABookThatWasNeverDeletedIsNotAnError()
    {
        var id = Seed(ProjectStage.Created);
        var service = NewService();

        await service.RestoreAsync(id, CancellationToken.None);

        Assert.Equal(id, Assert.Single(await service.ListAsync(CancellationToken.None)).Id);
    }

    /// <summary>The window closing without a turn is what makes the delete real (DogEarState's own rule).</summary>
    [Fact]
    public async Task TheRealDeleteRemovesItForGood()
    {
        var id = Seed(ProjectStage.Created);
        var service = NewService();
        await service.SoftDeleteAsync(id, CancellationToken.None);

        await service.DeleteAsync(id, CancellationToken.None);

        Assert.Null(await service.GetStatusAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task OtherBooksAreUntouchedByADelete()
    {
        var deleted = Seed(ProjectStage.Created);
        var kept = Seed(ProjectStage.Created);
        var service = NewService();

        await service.SoftDeleteAsync(deleted, CancellationToken.None);

        Assert.Equal(kept, Assert.Single(await service.ListAsync(CancellationToken.None)).Id);
    }
}
