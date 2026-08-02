using Microsoft.Extensions.Options;
using Moq;
using FocusPhone.Domain.Music;
using FocusPhone.Infrastructure.Music;
using Storyworthtelling.Voice;
using Xunit;

namespace FocusPhone.Tests.Music;

public class TasteProfileRefresherTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 20, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<ListeningEvent> SomeEvents() =>
    [
        new("t1", "Jazz", "Bill Evans", "Bill Evans", ListeningSignal.Completed, 20, "s1", Now),
        new("t2", "Jazz", "Bill Evans", "Bill Evans", ListeningSignal.Completed, 20, "s2", Now.AddMinutes(30)),
    ];

    private static IOptions<VoiceOptions> Key(string key) =>
        Microsoft.Extensions.Options.Options.Create(new VoiceOptions { ApiKey = key });

    [Fact]
    public async Task Refresh_NoKey_DoesNotCallClient_ReturnsStored()
    {
        var stored = new TasteProfile("Stored.", [], [], [], ["Jazz"], [], [], false, 3);
        var client = new Mock<ITasteProfileClient>(MockBehavior.Strict);
        var store = new Mock<ITasteProfileStore>();
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        var result = await new TasteProfileRefresher(client.Object, store.Object, Key(""))
            .RefreshAsync(SomeEvents(), CancellationToken.None);

        Assert.Same(stored, result);
        client.VerifyNoOtherCalls();
        store.Verify(s => s.SaveAsync(It.IsAny<TasteProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refresh_NoEvents_DoesNotCallClient()
    {
        var stored = TasteProfile.Empty;
        var client = new Mock<ITasteProfileClient>(MockBehavior.Strict);
        var store = new Mock<ITasteProfileStore>();
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        var result = await new TasteProfileRefresher(client.Object, store.Object, Key("sk-test"))
            .RefreshAsync([], CancellationToken.None);

        Assert.Same(stored, result);
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refresh_HappyPath_RefinesAndSaves()
    {
        var refined = new TasteProfile("Refined.", [], [], [], ["Jazz"], ["Bill Evans"], [], false, 2);
        var client = new Mock<ITasteProfileClient>();
        client.Setup(c => c.RefineAsync(It.IsAny<TasteProfile>(), It.IsAny<TasteEvidence>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refined);
        var store = new Mock<ITasteProfileStore>();
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(TasteProfile.Empty);

        var result = await new TasteProfileRefresher(client.Object, store.Object, Key("sk-test"))
            .RefreshAsync(SomeEvents(), CancellationToken.None);

        Assert.Same(refined, result);
        store.Verify(s => s.SaveAsync(refined, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Refresh_ClientThrows_ReturnsExisting_DoesNotSave()
    {
        var existing = new TasteProfile("Existing.", [], [], [], [], [], [], false, 5);
        var client = new Mock<ITasteProfileClient>();
        client.Setup(c => c.RefineAsync(It.IsAny<TasteProfile>(), It.IsAny<TasteEvidence>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));
        var store = new Mock<ITasteProfileStore>();
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await new TasteProfileRefresher(client.Object, store.Object, Key("sk-test"))
            .RefreshAsync(SomeEvents(), CancellationToken.None);

        Assert.Same(existing, result);
        store.Verify(s => s.SaveAsync(It.IsAny<TasteProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
