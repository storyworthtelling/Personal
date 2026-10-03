using System.Text.RegularExpressions;
using Index.Domain.Voice;
using Index.Infrastructure.Voice;
using Index.Tests.Support;
using Storyworthtelling.Voice;

namespace Index.Tests.Voice;

/// <summary>
/// R-G29 (b)-(d): the exact prompt the composer sends, and the seat's behaviour around it — hidden (a null
/// line) when there is no profile, no key, no facts, no signed-in account or the call fails; and composed
/// once per (seat, facts, day) so a re-render or a tab switch is not another model call.
///
/// <para>The bugs these reintroduce are the two that cost something with nothing visibly wrong: a balance
/// in the prompt (the sentence reads fine and the model has been handed a figure it must not say), and
/// composing on every render (every call succeeds, every reply is correct, and the bill is the only
/// symptom).</para>
/// </summary>
public class VoiceSeatComposerTests
{
    private readonly RecordingVoiceClient client = new();
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly CapturingLogger<VoiceSeatComposer> logger = new();
    private VoiceProfile profile = VoiceTestSupport.Profile();
    private VoiceConfig config = new("not-a-real-key");
    private bool signedIn = true;

    private VoiceSeatComposer Composer() => new(
        new VoicedMessenger(new FakeProfileStore(profile), client),
        new DriveVoiceOptions(new FakeConfigStore(config)),
        clock,
        () => signedIn,
        logger);

    private static VoiceFacts Snowball() => VoiceFactBuilder.Snowball(VoiceTestSupport.TwelvePercentDown());

    // ---- (b) the prompt carries the facts and nothing else ----

    [Fact]
    public async Task TheAccountsAndSnowballPromptsCarryNoFigureNoNameAndNoMask()
    {
        var composer = Composer();
        await composer.ComposeAsync(VoiceFactBuilder.Accounts(VoiceTestSupport.TotalsThenNow(90000.50m, 88000m)), default);
        await composer.ComposeAsync(Snowball(), default);

        Assert.Equal(2, client.Requests.Count);
        foreach (var request in client.Requests)
        {
            var sent = request.Purpose + "\n" + request.Context;
            Assert.DoesNotMatch(@"\$\d", sent);
            Assert.DoesNotMatch(@"\d", sent);                       // not one digit: words, percentages, counts
            AssertNoneOf(sent, "Redwood Bank", "Bluebird", "Meridian Card", "Harbor Auto Finance", "Northfield Student Loans", "Cedar Hollow Bank", "•", "····");
            Assert.DoesNotMatch(@"\b\d{4}\b", sent);                // a mask
        }
    }

    [Fact]
    public async Task TheSavingsPromptCarriesTheRateAndNoBalanceGoalOrName()
    {
        await Composer().ComposeAsync(VoiceFactBuilder.Savings(VoiceTestSupport.SavingsAccounts()), default);

        var sent = Assert.Single(client.Requests);
        var all = sent.Purpose + "\n" + sent.Context;

        Assert.DoesNotMatch(@"\$\d", all);
        Assert.Equal(["220"], Regex.Matches(all, @"\d+").Select(match => match.Value).ToArray());
        AssertNoneOf(all, "8200", "8,200", "14000", "14,000", "1350", "1,350", "9550", "9,550",
            "Redwood Bank", "Lakeside", "Share", "187", "32.00");
        Assert.Contains("only the monthly rate", sent.Context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRequestIsMarkedHighVisibilityAndNamesTheSeatsOccasion()
    {
        await Composer().ComposeAsync(Snowball(), default);

        var sent = Assert.Single(client.Requests);
        Assert.Equal(VoicedOccasions.For(VoiceSeat.Snowball), sent.Purpose);
        Assert.True(sent.HighVisibility);
        Assert.Equal(VoiceSeatPrompts.Build(Snowball())!.Context, sent.Context);
    }

    // ---- (c) hidden when there is nothing to say it with ----

    [Fact]
    public async Task AComposedSentenceIsReturned()
    {
        Assert.Equal("Twelve percent down since last November's peak.", await Composer().ComposeAsync(Snowball(), default));
    }

    [Fact]
    public async Task NoProfileMeansNoLineAndNoModelCall()
    {
        profile = VoiceProfile.Empty;

        Assert.Null(await Composer().ComposeAsync(Snowball(), default));
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task AProfileNeedingRelearnIsTreatedAsNoProfile()
    {
        profile = VoiceProfile.Empty with { NeedsRelearn = true };

        Assert.Null(await Composer().ComposeAsync(Snowball(), default));
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task NoKeyMeansNoLineAndNoModelCall()
    {
        config = VoiceConfig.Empty;

        Assert.Null(await Composer().ComposeAsync(Snowball(), default));
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task NoFactsMeansNoLineAndNoModelCall()
    {
        Assert.Null(await Composer().ComposeAsync(VoiceFacts.None(VoiceSeat.Accounts), default));
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task NoSignedInAccountMeansNoLineAndNoModelCall()
    {
        signedIn = false;

        Assert.Null(await Composer().ComposeAsync(Snowball(), default));
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task AFailedCallIsAHiddenLineNotAnException()
    {
        client.Failure = new HttpRequestException("the model is down");

        Assert.Null(await Composer().ComposeAsync(Snowball(), default));
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task ABlankReplyIsAHiddenLine()
    {
        client.Reply = "  ";

        Assert.Null(await Composer().ComposeAsync(Snowball(), default));
    }

    [Fact]
    public async Task AReplyThatStatesAnAmountOnASeatThatSpeaksNoneIsHidden()
    {
        client.Reply = "You owe $88,000 now.";

        Assert.Null(await Composer().ComposeAsync(Snowball(), default));
    }

    // ---- (d) once per (seat, facts, day) ----

    [Fact]
    public async Task ASecondRenderOnTheSameDayMakesNoSecondCall()
    {
        var composer = Composer();

        var first = await composer.ComposeAsync(Snowball(), default);
        var second = await composer.ComposeAsync(Snowball(), default);
        var third = await composer.ComposeAsync(Snowball(), default);

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task TheNextDayComposesAgain()
    {
        var composer = Composer();
        await composer.ComposeAsync(Snowball(), default);

        clock.Advance(TimeSpan.FromDays(1));
        await composer.ComposeAsync(Snowball(), default);

        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task ChangedFactsComposeAgainAndAnotherSeatHasItsOwnEntry()
    {
        var composer = Composer();
        await composer.ComposeAsync(Snowball(), default);
        await composer.ComposeAsync(VoiceFactBuilder.Snowball(
            VoiceTestSupport.History([new(2025, 11, 7), new(2026, 9, 25)], ("A", false, [1000m, 700m]))), default);
        await composer.ComposeAsync(new VoiceFacts(VoiceSeat.Accounts, Snowball().Lines), default);

        Assert.Equal(3, client.Requests.Count);
    }

    [Fact]
    public async Task AFailureIsRememberedBrieflyThenTriedAgain()
    {
        client.Failure = new HttpRequestException("the model is down");
        var composer = Composer();

        await composer.ComposeAsync(Snowball(), default);
        clock.Advance(TimeSpan.FromMinutes(VoiceSeatComposer.FailureMemoryMinutes - 1));
        await composer.ComposeAsync(Snowball(), default);
        Assert.Single(client.Requests);            // a tab switch right after a failure is not a retry storm

        clock.Advance(TimeSpan.FromMinutes(2));
        client.Failure = null;
        Assert.Equal("Twelve percent down since last November's peak.", await composer.ComposeAsync(Snowball(), default));
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task ConcurrentRendersOfTheSameSeatMakeOneCall()
    {
        var composer = Composer();

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => composer.ComposeAsync(Snowball(), default)));

        Assert.Single(client.Requests);
    }

    // ---- (g) nothing of the prompt, the profile or the sentence reaches the log ----

    [Fact]
    public async Task NothingOfThePromptTheProfileOrTheSentenceIsLogged()
    {
        var composer = Composer();
        await composer.ComposeAsync(Snowball(), default);                       // composed
        client.Reply = "You owe $88,000 now.";
        await composer.ComposeAsync(VoiceFactBuilder.Accounts(VoiceTestSupport.TotalsThenNow(90000m, 88000m)), default);
        profile = VoiceProfile.Empty;
        await composer.ComposeAsync(VoiceFactBuilder.Savings(VoiceTestSupport.SavingsAccounts()), default);  // no profile

        var logged = string.Join('\n', logger.Entries);
        AssertNoneOf(logged, VoiceTestSupport.Sentinel, "twelve percent", "Twelve percent", "November", "paid off",
            "88,000", "$", "320", "dollars", "FACTS", "Redwood Bank", "not-a-real-key", "one short sentence");
    }

    private static void AssertNoneOf(string text, params string[] forbidden)
    {
        foreach (var item in forbidden)
        {
            Assert.False(text.Contains(item, StringComparison.OrdinalIgnoreCase),
                $"\"{item}\" must not appear, but it did in:\n{text}");
        }
    }
}
