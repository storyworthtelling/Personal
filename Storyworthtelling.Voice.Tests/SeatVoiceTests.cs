using Moq;
using Storyworthtelling.CallSheet;
using Storyworthtelling.Voice;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

/// <summary>
/// One seat on the Call Sheet, written by the suite's one rule (audit W10.3): its purpose, words and tier from the
/// sheet, its facts as the context, once per facts and day, kept across a restart.
/// </summary>
public sealed class SeatVoiceTests : IDisposable
{
    private static readonly VoiceLine Seat = CallSheet.CallSheet.For("Foreword", "checklist-coach");
    private static readonly DateTimeOffset Morning = new(2026, 3, 9, 6, 0, 0, TimeSpan.Zero);

    private readonly string folder = Directory.CreateTempSubdirectory("seat-voice-").FullName;
    private readonly Mock<IVoiceProfileStore> store = new();
    private readonly Mock<IVoiceClient> client = new();
    private readonly List<MessageRequest> sent = [];
    private int refreshed;

    public SeatVoiceTests()
    {
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(VoiceProfile.Empty with { Register = "dry", DocumentsAnalyzed = 1 });
        client.Setup(c => c.ComposeAsync(It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<VoiceProfile, MessageRequest, CancellationToken>((_, request, _) => sent.Add(request))
            .ReturnsAsync("Three on the list. Begin anywhere.");
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private SeatVoice Voice(DateTimeOffset? now = null) => new(
        new VoicedMessenger(store.Object, client.Object),
        _ => { refreshed++; return Task.CompletedTask; },
        new SeatMemoryFile(Path.Combine(folder, "voice-seats.json")),
        () => now ?? Morning);

    [Fact]
    public async Task TheRequestIsTheSheetsLineAndTheFactsInOrder()
    {
        var line = await Voice().ComposeAsync(Seat, ["Three tasks are on the list.", "One is carried over."], default);

        Assert.Equal("Three on the list. Begin anywhere.", line);
        var request = Assert.Single(sent);
        Assert.Equal(Seat.Purpose, request.Purpose);
        Assert.Equal(Seat.MaxWords, request.MaxWords);
        Assert.Equal(Seat.HighVisibility, request.HighVisibility);
        Assert.Equal(Seat.Seat, request.Seat);
        Assert.True(request.Context.IndexOf("Three tasks", StringComparison.Ordinal)
            < request.Context.IndexOf("carried over", StringComparison.Ordinal));
        Assert.Equal(1, refreshed);
    }

    [Fact]
    public async Task TheSameFactsTheSameDayAreOneCallEvenAcrossARestart()
    {
        await Voice().ComposeAsync(Seat, ["Three tasks are on the list."], default);
        var again = await Voice(Morning.AddHours(3)).ComposeAsync(Seat, ["Three tasks are on the list."], default);

        Assert.Equal("Three on the list. Begin anywhere.", again);
        Assert.Single(sent);
    }

    [Fact]
    public async Task AHiddenSeatWithNoFactsAsksNothing()
    {
        Assert.Equal(string.Empty, await Voice().ComposeAsync(Seat, [], default));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task NoVoiceIsAnEmptyLineTriedAgainSoon()
    {
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(VoiceProfile.Empty);

        Assert.Equal(string.Empty, await Voice().ComposeAsync(Seat, ["Three tasks are on the list."], default));
        Assert.Equal(string.Empty, await Voice(Morning.AddMinutes(2)).ComposeAsync(Seat, ["Three tasks are on the list."], default));
        Assert.Equal(1, refreshed);

        await Voice(Morning + SeatPlan.RetryAfter).ComposeAsync(Seat, ["Three tasks are on the list."], default);
        Assert.Equal(2, refreshed);
    }

    [Fact]
    public async Task AKeyThatCannotBeReadIsAnEmptyLineNotAnException()
    {
        var voice = new SeatVoice(
            new VoicedMessenger(store.Object, client.Object),
            _ => throw new HttpRequestException("offline"),
            new SeatMemoryFile(Path.Combine(folder, "voice-seats.json")),
            () => Morning);

        Assert.Equal(string.Empty, await voice.ComposeAsync(Seat, ["Three tasks are on the list."], default));
    }
}
