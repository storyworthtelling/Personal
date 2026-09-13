using Microsoft.Extensions.Options;
using Moq;
using Folio.Domain.External;
using Folio.Domain.Model;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Imaging;
using Folio.Infrastructure.Services;
using Folio.Tests.Support;
using Xunit;

namespace Folio.Tests.Covers;

/// <summary>
/// A book's jacket: painted by the model, replaced by the owner, and never lost by either.
///
/// <para><b>Every cover is written to a new file.</b> Nothing is overwritten in place, which is what
/// makes the undo behind a repaint honest: the fold can put the old jacket back because the old
/// jacket is still on disk. Plate reached the same conclusion about its darkroom — the bytes have to
/// outlive the row by the length of the fold, or the undo is a lie.</para>
///
/// <para><b>Whose cover it is, is recorded.</b> A jacket the owner chose is not the same as one the
/// app painted, and repainting over the first is the case worth being careful about.</para>
/// </summary>
public class CoverTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly Mock<IGoogleDocsClient> docs = new();
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "folio-covers", Guid.NewGuid().ToString("N"));
    private readonly FileSystemImageStore imageStore;

    public CoverTests() =>
        imageStore = new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot }));

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }

    private ProjectService NewService() =>
        new(database, docs.Object, imageStore, clock, Options.Create(new RateLimitOptions()));

    private Guid Seed()
    {
        using var context = database.NewContext();
        var novel = new Novel
        {
            Title = "Quest", GoogleDocId = "doc", Stage = ProjectStage.Completed,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow,
        };
        novel.ProjectId = novel.Id;
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    // A book with no jacket is the ordinary case, not a missing file: it falls back to plain cloth.
    [Fact]
    public async Task ABookStartsWithNoJacket()
    {
        var status = await NewService().GetStatusAsync(Seed(), CancellationToken.None);

        Assert.Null(status!.CoverPath);
        Assert.False(status.CoverIsOwners);
    }

    [Fact]
    public async Task ReplacingTheCoverStoresItAsTheOwners()
    {
        var id = Seed();
        var service = NewService();

        await service.ReplaceCoverAsync(id, [1, 2, 3], CancellationToken.None);

        var status = await service.GetStatusAsync(id, CancellationToken.None);
        Assert.NotNull(status!.CoverPath);
        Assert.True(status.CoverIsOwners);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(status.CoverPath!));
    }

    [Fact]
    public async Task RepaintingStoresItAsThePaintedOne()
    {
        var id = Seed();
        var service = NewService();

        await service.SetPaintedCoverAsync(id, [9, 9, 9], CancellationToken.None);

        var status = await service.GetStatusAsync(id, CancellationToken.None);
        Assert.NotNull(status!.CoverPath);
        Assert.False(status.CoverIsOwners);
    }

    /// <summary>
    /// The heart of it: repainting over a cover the owner chose leaves the old one on disk, so the
    /// fold can put it back. This is why nothing asks before repainting.
    /// </summary>
    [Fact]
    public async Task RepaintingOverTheOwnersCoverLeavesTheOldOneOnDisk()
    {
        var id = Seed();
        var service = NewService();
        await service.ReplaceCoverAsync(id, [1, 2, 3], CancellationToken.None);
        var theirs = (await service.GetStatusAsync(id, CancellationToken.None))!.CoverPath;

        await service.SetPaintedCoverAsync(id, [9, 9, 9], CancellationToken.None);

        var now = (await service.GetStatusAsync(id, CancellationToken.None))!.CoverPath;
        Assert.NotEqual(theirs, now);
        Assert.True(
            File.Exists(theirs!),
            "The owner's jacket was overwritten, so the undo behind a repaint would be a lie.");
    }

    [Fact]
    public async Task PuttingTheOldCoverBackRestoresBothTheFileAndWhoseItWas()
    {
        var id = Seed();
        var service = NewService();
        await service.ReplaceCoverAsync(id, [1, 2, 3], CancellationToken.None);
        var theirs = (await service.GetStatusAsync(id, CancellationToken.None))!.CoverPath;
        await service.SetPaintedCoverAsync(id, [9, 9, 9], CancellationToken.None);

        await service.RestoreCoverAsync(id, theirs, wasOwners: true, CancellationToken.None);

        var status = await service.GetStatusAsync(id, CancellationToken.None);
        Assert.Equal(theirs, status!.CoverPath);
        Assert.True(status.CoverIsOwners);
    }

    // Undoing back to having had no jacket at all has to work too, or the first repaint is one-way.
    [Fact]
    public async Task TheUndoCanGoBackToNoJacket()
    {
        var id = Seed();
        var service = NewService();
        await service.SetPaintedCoverAsync(id, [9, 9, 9], CancellationToken.None);

        await service.RestoreCoverAsync(id, null, wasOwners: false, CancellationToken.None);

        Assert.Null((await service.GetStatusAsync(id, CancellationToken.None))!.CoverPath);
    }

    [Fact]
    public async Task EachCoverGetsItsOwnFile()
    {
        var id = Seed();
        var service = NewService();

        await service.ReplaceCoverAsync(id, [1], CancellationToken.None);
        var first = (await service.GetStatusAsync(id, CancellationToken.None))!.CoverPath;
        await service.ReplaceCoverAsync(id, [2], CancellationToken.None);
        var second = (await service.GetStatusAsync(id, CancellationToken.None))!.CoverPath;

        Assert.NotEqual(first, second);
    }

    // Deleting the book takes its jackets with it — they live under the novel's own folder.
    [Fact]
    public async Task AJacketLivesUnderItsOwnBook()
    {
        var id = Seed();
        var service = NewService();

        await service.ReplaceCoverAsync(id, [1, 2, 3], CancellationToken.None);

        var path = (await service.GetStatusAsync(id, CancellationToken.None))!.CoverPath;
        Assert.Contains(id.ToString(), path, StringComparison.Ordinal);
    }
}
