using Moq;
using Storyworthtelling.Voice;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

public class VoicedMessengerTests
{
    private static readonly VoiceProfile Learned = VoiceProfile.Empty with
    {
        Register = "Wry and warm, economical.",
        Traits = ["wry"],
        Examples = [new VoiceExample("At last, the gate.", Guid.NewGuid())],
        Avoid = ["hype"],
        DocumentsAnalyzed = 3,
    };

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

    [Fact]
    public async Task AFailedSeatWarnsOnceADayNotEveryPaint()
    {
        // A rejected key on every voiced seat in every app was logged at Debug, which nothing keeps (audit 08 §22).
        var warnings = new List<string>();
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Learned);
        client.Setup(c => c.ComposeAsync(Learned, It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Storyworthtelling.Voice.Claude.ClaudeRequestException(
                Storyworthtelling.Voice.Claude.ClaudeFailure.KeyRejected, "invalid x-api-key", System.Net.HttpStatusCode.Unauthorized));
        var messenger = new VoicedMessenger(store.Object, client.Object, warn: warnings.Add);
        var seat = "seat-" + Guid.NewGuid().ToString("N");

        await messenger.ComposeAsync(new MessageRequest(seat), "Welcome.", CancellationToken.None);
        await messenger.ComposeAsync(new MessageRequest(seat), "Welcome.", CancellationToken.None);
        await messenger.ComposeAsync(new MessageRequest(seat + "-other"), "Welcome.", CancellationToken.None);

        Assert.Equal(2, warnings.Count);
        Assert.Equal($"CLAUDE voice {seat} failed: KeyRejected 401", warnings[0]);
    }
}
