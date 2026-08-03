using Moq;
using Storyworthtelling.Voice;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

public class VoicedMessengerTests
{
    private static readonly VoiceProfile Learned =
        new("Wry and warm, economical.", ["wry"], ["At last, the gate."], ["hype"], [], DocumentsAnalyzed: 3);

    private readonly Mock<IVoiceProfileStore> store = new();
    private readonly Mock<IVoiceClient> client = new();

    private VoicedMessenger Messenger() => new(store.Object, client.Object);

    [Fact]
    public async Task ComposesInTheOwnersVoice_WhenAProfileExists()
    {
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Learned);
        client.Setup(c => c.ComposeAsync(Learned, It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("  Welcome back — let's make something.  ");

        var message = await Messenger().ComposeAsync(
            new MessageRequest("welcome"), fallback: "Welcome.", CancellationToken.None);

        Assert.Equal("Welcome back — let's make something.", message); // trimmed
    }

    [Fact]
    public async Task FallsBackToNeutralCopy_WhenNoProfileHasBeenLearnedYet()
    {
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(VoiceProfile.Empty);

        var message = await Messenger().ComposeAsync(
            new MessageRequest("welcome"), fallback: "Welcome.", CancellationToken.None);

        Assert.Equal("Welcome.", message);
        // No point paying for a generation with no voice to write in.
        client.Verify(c => c.ComposeAsync(It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FallsBackToNeutralCopy_WhenGenerationReturnsNothing()
    {
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Learned);
        client.Setup(c => c.ComposeAsync(It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("   ");

        var message = await Messenger().ComposeAsync(
            new MessageRequest("welcome"), fallback: "Welcome.", CancellationToken.None);

        Assert.Equal("Welcome.", message);
    }

    [Fact]
    public async Task FallsBackToNeutralCopy_WhenTheVoiceServiceThrows()
    {
        // No Claude key, offline, a transient model error — the screen must never break.
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Learned);
        client.Setup(c => c.ComposeAsync(It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));

        var message = await Messenger().ComposeAsync(
            new MessageRequest("welcome"), fallback: "Welcome.", CancellationToken.None);

        Assert.Equal("Welcome.", message);
    }

    [Fact]
    public async Task DoesNotSwallowCancellation()
    {
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Messenger().ComposeAsync(new MessageRequest("welcome"), "Welcome.", CancellationToken.None));
    }
}
