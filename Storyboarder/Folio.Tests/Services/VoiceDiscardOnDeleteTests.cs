using Folio.Domain.External;
using Folio.Domain.Model;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Imaging;
using Folio.Infrastructure.Services;
using Folio.Tests.Support;
using Microsoft.Extensions.Options;
using Moq;
using Storyworthtelling.Voice;

namespace Folio.Tests.Services;

/// <summary>
/// The other half of discard-and-relearn: something has to call it. When a book leaves the library,
/// the phrasings it taught the owner's voice leave with it.
///
/// <para>The bug this reintroduces is discarding on <i>any</i> draft delete. A draft is not a book —
/// a project has many — so tidying up draft 2 of a book the owner is still writing would quietly
/// strip that book's anchors out of the shared voice, and the only symptom is that app messages
/// gradually stop sounding like them. Nothing fails, and the profile is on Drive where no test
/// looks.</para>
/// </summary>
public class VoiceDiscardOnDeleteTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly string outputRoot =
        Path.Combine(Path.GetTempPath(), "folio-voice-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IVoiceProfileStore> voice = new();

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DeletingTheLastDraftOfABookDiscardsWhatItTaught()
    {
        var (project, only) = SeedBook(drafts: 1);
        var saved = Learned(project);

        await NewService().DeleteAsync(only, CancellationToken.None);

        var after = Assert.Single(saved.Writes);
        Assert.DoesNotContain(after.Examples, example => example.ProjectId == project);
        Assert.Contains(after.Examples, example => example.Text == "from another book");
    }

    [Fact]
    public async Task DeletingOneDraftOfABookStillBeingWrittenLeavesTheVoiceAlone()
    {
        var (project, first) = SeedBook(drafts: 2);
        var saved = Learned(project);

        await NewService().DeleteAsync(first, CancellationToken.None);

        Assert.Empty(saved.Writes);
    }

    /// <summary>
    /// A book that never taught the voice anything writes nothing back — a Drive round trip for no
    /// change is still a Drive round trip, and it can fail.
    /// </summary>
    [Fact]
    public async Task DeletingABookThatTaughtNothingWritesNothingBack()
    {
        var (_, only) = SeedBook(drafts: 1);
        var saved = Learned(Guid.NewGuid());

        await NewService().DeleteAsync(only, CancellationToken.None);

        Assert.Empty(saved.Writes);
    }

    /// <summary>
    /// The voice is a nicety; the delete is the owner's instruction. A Drive that is unreachable, or
    /// a profile that cannot be read, must not leave the book sitting in the library.
    /// </summary>
    [Fact]
    public async Task ADriveFailureDoesNotStopTheDelete()
    {
        var (_, only) = SeedBook(drafts: 1);
        voice.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Drive is unreachable"));

        await NewService().DeleteAsync(only, CancellationToken.None);

        using var context = database.NewContext();
        Assert.Empty(context.Novels);
    }

    [Fact]
    public async Task AHeadWithNoVoiceStoreStillDeletes()
    {
        var (_, only) = SeedBook(drafts: 1);

        await NewService(withVoice: false).DeleteAsync(only, CancellationToken.None);

        using var context = database.NewContext();
        Assert.Empty(context.Novels);
    }

    /// <summary>A profile holding anchors from this book and one other.</summary>
    private Recorder Learned(Guid project)
    {
        var recorder = new Recorder();
        var profile = VoiceProfile.Empty with
        {
            Register = "Wry, warm, economical",
            Examples =
            [
                new VoiceExample("the tide was out", project),
                new VoiceExample("from another book", Guid.NewGuid()),
            ],
            DocumentsAnalyzed = 2,
        };

        voice.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        voice.Setup(store => store.SaveAsync(It.IsAny<VoiceProfile>(), It.IsAny<CancellationToken>()))
            .Callback((VoiceProfile written, CancellationToken _) => recorder.Writes.Add(written))
            .Returns(Task.CompletedTask);
        return recorder;
    }

    private ProjectService NewService(bool withVoice = true) => new(
        database,
        new Mock<IGoogleDocsClient>().Object,
        new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot })),
        new FakeClock(DateTimeOffset.UnixEpoch),
        Options.Create(new RateLimitOptions()),
        withVoice ? voice.Object : null);

    /// <summary>One book with the given number of drafts; returns the project id and the first draft.</summary>
    private (Guid Project, Guid FirstDraft) SeedBook(int drafts)
    {
        using var context = database.NewContext();
        var first = new Novel { Title = "The Salt Line", GoogleDocId = "doc", DraftNumber = 1 };
        first.ProjectId = first.Id;
        context.Novels.Add(first);

        for (var n = 2; n <= drafts; n++)
        {
            context.Novels.Add(new Novel
            {
                Title = "The Salt Line", GoogleDocId = "doc", DraftNumber = n,
                ProjectId = first.ProjectId, PreviousDraftId = first.Id,
            });
        }

        context.SaveChanges();
        return (first.ProjectId, first.Id);
    }

    private sealed class Recorder
    {
        public List<VoiceProfile> Writes { get; } = [];
    }
}
