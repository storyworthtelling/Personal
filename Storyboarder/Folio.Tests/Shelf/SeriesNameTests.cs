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
/// A series has a name, and the owner can change it.
///
/// <para>It was going to be derived from the first book's title, which is how a series is usually
/// named and is wrong as a rule: a trilogy is often called something the first book is not, the
/// first book can be retitled without renaming the series, and a series can be named before anyone
/// knows which book will open it. So the name is stored, and it is the owner's.</para>
///
/// <para>Null means "not named yet" and falls back to the first book's title, so every series that
/// already exists keeps the label it has until somebody decides otherwise.</para>
/// </summary>
public class SeriesNameTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly Mock<IGoogleDocsClient> docs = new();
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "folio-series", Guid.NewGuid().ToString("N"));
    private readonly FileSystemImageStore imageStore;

    public SeriesNameTests() =>
        imageStore = new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot }));

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }

    private ProjectService NewService() =>
        new(database, docs.Object, imageStore, clock, Options.Create(new RateLimitOptions()));

    private Guid SeedSeries(Guid seriesId, string title, int sequence, string? seriesName = null)
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = title, GoogleDocId = "doc", Stage = ProjectStage.Completed,
            SeriesId = seriesId, SeriesSequence = sequence, SeriesName = seriesName,
            ShelvedAtUtc = clock.UtcNow,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow,
        };
        novel.ProjectId = novel.Id;
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    [Fact]
    public async Task AnUnnamedSeriesIsCalledAfterItsFirstBook()
    {
        var series = Guid.NewGuid();
        SeedSeries(series, "The Salt Line", 1);
        SeedSeries(series, "Saltmarsh", 2);

        var shelved = await NewService().ListShelvedAsync(CancellationToken.None);

        Assert.All(shelved, book => Assert.Equal("The Salt Line", book.SeriesName));
    }

    [Fact]
    public async Task RenamingItChangesWhatEveryBookInItReports()
    {
        var series = Guid.NewGuid();
        SeedSeries(series, "The Salt Line", 1);
        SeedSeries(series, "Saltmarsh", 2);
        var service = NewService();

        await service.RenameSeriesAsync(series, "The Tidewater Books", CancellationToken.None);

        var shelved = await service.ListShelvedAsync(CancellationToken.None);
        Assert.All(shelved, book => Assert.Equal("The Tidewater Books", book.SeriesName));
    }

    // Retitling book one must not silently rename the series once it has been named.
    [Fact]
    public async Task ANamedSeriesKeepsItsNameWhateverTheFirstBookIsCalled()
    {
        var series = Guid.NewGuid();
        var first = SeedSeries(series, "The Salt Line", 1, seriesName: "The Tidewater Books");

        using (var context = database.NewContext())
        {
            var novel = context.Novels.Single(n => n.Id == first);
            novel.Title = "Something Else Entirely";
            context.SaveChanges();
        }

        var shelved = await NewService().ListShelvedAsync(CancellationToken.None);
        Assert.All(shelved, book => Assert.Equal("The Tidewater Books", book.SeriesName));
    }

    [Fact]
    public async Task ClearingTheNameGoesBackToTheFirstBooksTitle()
    {
        var series = Guid.NewGuid();
        SeedSeries(series, "The Salt Line", 1, seriesName: "The Tidewater Books");
        var service = NewService();

        await service.RenameSeriesAsync(series, "   ", CancellationToken.None);

        var shelved = await service.ListShelvedAsync(CancellationToken.None);
        Assert.Equal("The Salt Line", Assert.Single(shelved).SeriesName);
    }

    [Fact]
    public async Task RenamingLeavesOtherSeriesAlone()
    {
        var one = Guid.NewGuid();
        var other = Guid.NewGuid();
        SeedSeries(one, "The Salt Line", 1);
        SeedSeries(other, "Northing", 1);
        var service = NewService();

        await service.RenameSeriesAsync(one, "The Tidewater Books", CancellationToken.None);

        var shelved = await service.ListShelvedAsync(CancellationToken.None);
        Assert.Equal("Northing", Assert.Single(shelved, book => book.Title == "Northing").SeriesName);
    }

    [Fact]
    public async Task AStandaloneBookHasNoSeriesName()
    {
        using (var context = database.NewContext())
        {
            var novel = new Novel
            {
                Title = "Fair Copy", GoogleDocId = "doc", Stage = ProjectStage.Completed,
                ShelvedAtUtc = clock.UtcNow, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow,
            };
            novel.ProjectId = novel.Id;
            context.Novels.Add(novel);
            context.SaveChanges();
        }

        Assert.Null(Assert.Single(await NewService().ListShelvedAsync(CancellationToken.None)).SeriesName);
    }
}
