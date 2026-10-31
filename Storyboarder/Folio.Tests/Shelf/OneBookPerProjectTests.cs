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
/// Drafts of one book are one book: the desk and the shelf each show a project once, by its newest
/// draft, and the book page is where an older draft's report is reached.
/// </summary>
public class OneBookPerProjectTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly Mock<IGoogleDocsClient> docs = new();
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "folio-onebook", Guid.NewGuid().ToString("N"));
    private readonly FileSystemImageStore imageStore;

    public OneBookPerProjectTests() =>
        imageStore = new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot }));

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }

    private ProjectService NewService() =>
        new(database, docs.Object, imageStore, clock, Options.Create(new RateLimitOptions()));

    private Guid Seed(Guid? project, int draft, bool shelved)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Quest", GoogleDocId = "doc", Stage = ProjectStage.Completed, DraftNumber = draft,
            CreatedAtUtc = clock.UtcNow.AddDays(draft), UpdatedAtUtc = clock.UtcNow,
            ShelvedAtUtc = shelved ? clock.UtcNow : null,
        };
        novel.ProjectId = project ?? novel.Id;
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    [Fact]
    public async Task ThreeDraftsOnTheDeskAreOneBookShownByTheNewest()
    {
        var first = Seed(null, 1, shelved: false);
        var project = ProjectOf(first);
        Seed(project, 2, shelved: false);
        var third = Seed(project, 3, shelved: false);

        var desk = await NewService().ListAsync(CancellationToken.None);

        Assert.Equal(third, Assert.Single(desk).Id);
    }

    [Fact]
    public async Task ADraftBeingReadBringsAShelvedBookBackToTheDeskOnly()
    {
        var first = Seed(null, 1, shelved: true);
        var second = Seed(ProjectOf(first), 2, shelved: false);
        var service = NewService();

        Assert.Equal(second, Assert.Single(await service.ListAsync(CancellationToken.None)).Id);
        Assert.Empty(await service.ListShelvedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AShelvedBookIsShownOnceByItsNewestDraft()
    {
        var first = Seed(null, 1, shelved: true);
        var second = Seed(ProjectOf(first), 2, shelved: true);
        var service = NewService();

        Assert.Equal(second, Assert.Single(await service.ListShelvedAsync(CancellationToken.None)).Id);
        Assert.Empty(await service.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EachDraftOfTheBookIsListedOnItsPageAndNewestIsLast()
    {
        var first = Seed(null, 1, shelved: false);
        var second = Seed(ProjectOf(first), 2, shelved: false);

        var book = await NewService().GetBookAsync(first, CancellationToken.None);

        Assert.Equal(second, book!.LatestDraftId);
        Assert.Equal([1, 2], book.Drafts.Select(draft => draft.Number));
    }

    private Guid ProjectOf(Guid id)
    {
        using var context = database.NewContext();
        return context.Novels.Single(novel => novel.Id == id).ProjectId;
    }
}
