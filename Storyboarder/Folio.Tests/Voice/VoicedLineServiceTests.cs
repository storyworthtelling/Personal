using Folio.Domain.External;
using Folio.Domain.Model;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Imaging;
using Folio.Infrastructure.Services;
using Folio.Tests.Support;
using Microsoft.Extensions.Options;
using Moq;
using Storyworthtelling.Voice;

namespace Folio.Tests.Voice;

/// <summary>
/// The per-project line as the desk actually gets it (D96): composed when the book has moved,
/// remembered when it has not.
///
/// <para>The bug this reintroduces is composing every time the desk asks. The shelf is the screen the
/// app opens on and it paints on every return to it, so that is a model call per book per glance —
/// expensive, silently, in a place no test measures and no error surfaces.</para>
/// </summary>
public class VoicedLineServiceTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly string outputRoot =
        Path.Combine(Path.GetTempPath(), "folio-line-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IVoiceClient> client = new();
    private readonly Mock<IVoiceProfileStore> store = new();

    public VoicedLineServiceTests()
    {
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            VoiceProfile.Empty with { Register = "Wry", DocumentsAnalyzed = 1 });
        client.Setup(c => c.ComposeAsync(
                It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Four readers in, and two of them are arguing.");
    }

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TheFirstAskComposesTheLineAndKeepsIt()
    {
        var id = Seed(ProjectStage.ComposingAnalysis);

        var line = await NewService().VoicedLineAsync(id, "Working on it.", CancellationToken.None);

        Assert.Equal("Four readers in, and two of them are arguing.", line);

        using var context = database.NewContext();
        var novel = context.Novels.Single(n => n.Id == id);
        Assert.Equal(line, novel.VoicedLine);
        Assert.Equal(ProjectStage.ComposingAnalysis, novel.VoicedLineStage);
    }

    [Fact]
    public async Task AskingAgainWithNothingChangedComposesNothing()
    {
        var id = Seed(ProjectStage.ComposingAnalysis);
        var service = NewService();

        await service.VoicedLineAsync(id, "Working on it.", CancellationToken.None);
        var second = await service.VoicedLineAsync(id, "Working on it.", CancellationToken.None);

        Assert.Equal("Four readers in, and two of them are arguing.", second);
        client.Verify(c => c.ComposeAsync(
            It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task AskingAfterTheBookMovesComposesAgain()
    {
        var id = Seed(ProjectStage.ComposingAnalysis);
        var service = NewService();
        await service.VoicedLineAsync(id, "Working on it.", CancellationToken.None);

        Advance(id, ProjectStage.Archiving);
        await service.VoicedLineAsync(id, "Working on it.", CancellationToken.None);

        client.Verify(c => c.ComposeAsync(
            It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// The voice is layered over neutral copy, never instead of it — and a fallback is not stored, or
    /// the desk would remember the neutral line as though it were the owner's and never try again.
    /// </summary>
    [Fact]
    public async Task NoVoiceYetLeavesTheNeutralLineAndRemembersNothing()
    {
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(VoiceProfile.Empty);
        var id = Seed(ProjectStage.ComposingAnalysis);

        var line = await NewService().VoicedLineAsync(id, "Working on it.", CancellationToken.None);

        Assert.Equal("Working on it.", line);

        using var context = database.NewContext();
        Assert.Empty(context.Novels.Single(n => n.Id == id).VoicedLine);
    }

    [Fact]
    public async Task AFailedComposeLeavesTheNeutralLine()
    {
        client.Setup(c => c.ComposeAsync(
                It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var id = Seed(ProjectStage.ComposingAnalysis);

        Assert.Equal("Working on it.",
            await NewService().VoicedLineAsync(id, "Working on it.", CancellationToken.None));
    }

    [Fact]
    public async Task AHeadWithNoVoiceAtAllGetsTheNeutralLine()
    {
        var id = Seed(ProjectStage.ComposingAnalysis);

        Assert.Equal("Working on it.",
            await NewService(withVoice: false).VoicedLineAsync(id, "Working on it.", CancellationToken.None));
    }

    private ProjectService NewService(bool withVoice = true) => new(
        database,
        new Mock<IGoogleDocsClient>().Object,
        new FileSystemImageStore(Options.Create(new StorageOptions { OutputRoot = outputRoot })),
        new FakeClock(DateTimeOffset.UnixEpoch),
        Options.Create(new RateLimitOptions()),
        withVoice ? store.Object : null,
        withVoice ? client.Object : null);

    private Guid Seed(ProjectStage stage)
    {
        using var context = database.NewContext();
        var novel = new Novel { Title = "The Salt Line", GoogleDocId = "doc", Stage = stage };
        novel.ProjectId = novel.Id;
        context.Novels.Add(novel);
        context.SaveChanges();
        return novel.Id;
    }

    private void Advance(Guid id, ProjectStage stage)
    {
        using var context = database.NewContext();
        context.Novels.Single(n => n.Id == id).Stage = stage;
        context.SaveChanges();
    }

    // ---- The line must not say a book is done when it is not ------------------------------------------
    //
    // It was told only "Stage: ComposingAnalysis", and wrote "Draft 1 is complete" over a book that was
    // part-way through its reads and had in fact stopped on an error.

    [Fact]
    public async Task ABookInProgressIsToldItIsNotFinished()
    {
        var id = Seed(ProjectStage.ComposingAnalysis);
        MessageRequest? sent = null;
        client.Setup(c => c.ComposeAsync(
                It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<VoiceProfile, MessageRequest, CancellationToken>((_, request, _) => sent = request)
            .ReturnsAsync("Still reading.");

        await NewService().VoicedLineAsync(id, "Working on it.", CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Contains("NOT finished", sent!.Context);
        Assert.Contains("Do not say or imply", sent.Context);
    }

    [Fact]
    public async Task ACompletedBookIsToldItIsDone()
    {
        var id = Seed(ProjectStage.Completed);
        MessageRequest? sent = null;
        client.Setup(c => c.ComposeAsync(
                It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<VoiceProfile, MessageRequest, CancellationToken>((_, request, _) => sent = request)
            .ReturnsAsync("All done.");

        await NewService().VoicedLineAsync(id, "Delivered.", CancellationToken.None);

        Assert.Contains("finished and delivered", sent!.Context);
    }

    [Fact]
    public async Task AStoppedBookGetsNoVoicedLine_EvenOneRememberedFromBefore()
    {
        var id = Seed(ProjectStage.ComposingAnalysis);
        using (var context = database.NewContext())
        {
            var novel = context.Novels.Single(n => n.Id == id);
            novel.VoicedLine = "Draft 1 is complete.";
            novel.VoicedLineStage = ProjectStage.ComposingAnalysis;
            novel.LastError = "Reading pass 'x': truncated";
            novel.StageAttempts = new RateLimitOptions().MaxAttempts;
            context.SaveChanges();
        }

        var line = await NewService().VoicedLineAsync(id, "Stopped · reading.", CancellationToken.None);

        Assert.Equal("Stopped · reading.", line);
        client.Verify(c => c.ComposeAsync(
            It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()), Times.Never());
    }
}
