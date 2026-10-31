using Constitution.Domain.Voice;
using Constitution.Infrastructure.Voice;
using Storyworthtelling.Voice;

namespace Constitution.Tests.Voice;

/// <summary>
/// C-9.2.1 — composing a seat through <see cref="VoicedMessenger"/>, and the promise around it: a
/// voiced sentence when there is a profile and a key, and otherwise the plain sentence from the same
/// facts. Never hidden, never blank, never an exception. Everything is faked; no key, no network.
///
/// <para>The bugs these reintroduce: a seat that returns nothing when the voice is unavailable (a
/// blank gap on Today), and a log line that carries the owner's profile or the household's facts.</para>
/// </summary>
public class SeatComposerTests
{
    private const string PlainSentence = "Strength at 5:30. Lentil soup tonight.";

    private readonly RecordingVoiceClient client = new();
    private readonly FakeProfileStore profileStore = new(VoiceTestSupport.Profile());
    private readonly FakeConfigStore configStore = new(new VoiceConfig("not-a-real-key"));
    private readonly CapturingLogger<SeatComposer> logger = new();

    private SeatComposer Composer() => new(
        new VoicedMessenger(profileStore, client),
        new DriveVoiceOptions(configStore),
        logger);

    private static SeatRequest Today() =>
        new(VoiceSeat.TodayTopLine, ["strength at 5:30", "lentil soup tonight"], PlainSentence);

    // ---- the voiced line ----

    [Fact]
    public async Task WithAProfileAndAKeyTheSeatIsVoiced()
    {
        var line = await Composer().ComposeAsync(Today(), default);

        Assert.Equal(VoiceTestSupport.Reply, line.Text);
        Assert.True(line.IsVoiced);
        Assert.Equal(VoiceSeat.TodayTopLine, line.Seat);
    }

    [Fact]
    public async Task TheReplyIsTrimmed()
    {
        client.Reply = "  Strength at five-thirty.\n";
        Assert.Equal("Strength at five-thirty.", (await Composer().ComposeAsync(Today(), default)).Text);
    }

    [Fact]
    public async Task TheRequestNamesTheSeatsOccasionAndCarriesTheFactsInOrderAndNothingElse()
    {
        await Composer().ComposeAsync(Today(), default);

        var sent = Assert.Single(client.Requests);
        var occasion = VoicedOccasions.For(VoiceSeat.TodayTopLine);
        Assert.Equal(occasion.Purpose, sent.Purpose);
        Assert.Equal(occasion.MaxWords, sent.MaxWords);
        Assert.True(sent.HighVisibility);

        var first = sent.Context.IndexOf("strength at 5:30", StringComparison.Ordinal);
        var second = sent.Context.IndexOf("lentil soup tonight", StringComparison.Ordinal);
        Assert.True(first >= 0 && second > first, "both facts, in order");
        Assert.DoesNotContain(PlainSentence, sent.Context, StringComparison.Ordinal);
        Assert.DoesNotContain(VoiceTestSupport.Sentinel, sent.Context + sent.Purpose, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachSeatSendsItsOwnPurpose()
    {
        var composer = Composer();
        foreach (var occasion in VoicedOccasions.All)
        {
            await composer.ComposeAsync(new SeatRequest(occasion.Seat, ["a fact"], "A plain sentence."), default);
        }

        Assert.Equal(VoicedOccasions.All.Select(occasion => occasion.Purpose).ToArray(),
            client.Requests.Select(request => request.Purpose).ToArray());
    }

    // ---- the plain sentence, never hidden ----

    [Fact]
    public async Task WithNoProfileTheSeatShowsItsPlainSentence()
    {
        var composer = new SeatComposer(
            new VoicedMessenger(new FakeProfileStore(VoiceProfile.Empty), client),
            new DriveVoiceOptions(configStore), logger);

        var line = await composer.ComposeAsync(Today(), default);

        Assert.Equal(PlainSentence, line.Text);
        Assert.False(line.IsVoiced);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task WithNoKeyTheSeatShowsItsPlainSentenceWithoutAModelCall()
    {
        var composer = new SeatComposer(
            new VoicedMessenger(profileStore, client),
            new DriveVoiceOptions(new FakeConfigStore(VoiceConfig.Empty)), logger);

        var line = await composer.ComposeAsync(Today(), default);

        Assert.Equal(PlainSentence, line.Text);
        Assert.False(line.IsVoiced);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task AFailingClientGivesThePlainSentence()
    {
        client.Failure = new HttpRequestException("503 from the model");

        var line = await Composer().ComposeAsync(Today(), default);

        Assert.Equal(PlainSentence, line.Text);
        Assert.False(line.IsVoiced);
    }

    [Fact]
    public async Task AnUnreadableProfileGivesThePlainSentence()
    {
        profileStore.Failure = new IOException("Drive is offline");

        var line = await Composer().ComposeAsync(Today(), default);

        Assert.Equal(PlainSentence, line.Text);
        Assert.False(line.IsVoiced);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task UnreadableVoiceSettingsGiveThePlainSentence()
    {
        configStore.Failure = new UnauthorizedAccessException("wrong passphrase");

        var line = await Composer().ComposeAsync(Today(), default);

        Assert.Equal(PlainSentence, line.Text);
        Assert.False(line.IsVoiced);
        Assert.Empty(client.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    public async Task ABlankReplyGivesThePlainSentence(string reply)
    {
        client.Reply = reply;

        var line = await Composer().ComposeAsync(Today(), default);

        Assert.Equal(PlainSentence, line.Text);
        Assert.False(line.IsVoiced);
    }

    [Fact]
    public async Task ACallerWhoLeavesIsNotAnError()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        client.Failure = new OperationCanceledException(cancelled.Token);

        // Cancellation is the caller leaving the screen, not a failure to absorb into a sentence.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Composer().ComposeAsync(Today(), cancelled.Token));
    }

    // ---- what reaches a log ----

    [Fact]
    public async Task AFallbackIsLoggedBySeatIdAndKindOfFailureAndNothingElse()
    {
        client.Failure = new HttpRequestException("secret detail the model said");
        configStore.Failure = new IOException("drive detail");

        await Composer().ComposeAsync(Today(), default);

        var logged = string.Join("\n", logger.Entries);
        Assert.Contains("today-top-line", logged, StringComparison.Ordinal);
        Assert.Contains(nameof(IOException), logged, StringComparison.Ordinal);
        Assert.DoesNotContain("drive detail", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("secret detail", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("lentil", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("5:30", logged, StringComparison.Ordinal);
        Assert.DoesNotContain(VoiceTestSupport.Sentinel, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(VoiceTestSupport.Reply, logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVoicedLineLogsNothingOfItsContent()
    {
        await Composer().ComposeAsync(Today(), default);

        var logged = string.Join("\n", logger.Entries);
        Assert.DoesNotContain("lentil", logged, StringComparison.Ordinal);
        Assert.DoesNotContain(VoiceTestSupport.Reply, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(VoiceTestSupport.Sentinel, logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheComposerWorksWithNoLogger()
    {
        var composer = new SeatComposer(new VoicedMessenger(profileStore, client), new DriveVoiceOptions(configStore));
        Assert.True((await composer.ComposeAsync(Today(), default)).IsVoiced);
    }
}
