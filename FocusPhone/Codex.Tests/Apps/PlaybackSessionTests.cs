using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

/// <summary>
/// The playback sessions Flyleaf relays to Pulp (P12 Release F, finding 2.3, D30, D182): only for the
/// packages Pulp named, only when something changed, and with the title only for those. Titles and
/// packages here are invented.
/// </summary>
public class PlaybackSessionTests
{
    private const string Streamer = "com.example.streamer";
    private const string Other = "com.example.musicbox";

    private static readonly HiddenApps Named = HiddenApps.Of([Streamer]);

    private static PlaybackSessionReading Reading(
        string package = Streamer,
        string? title = "Harbor Lights",
        long positionMs = 60000,
        long durationMs = 2640000,
        PlaybackSessionState state = PlaybackSessionState.Playing) =>
        new(package, title, positionMs, durationMs, state);

    [Fact]
    public void ARoundTripKeepsEveryField()
    {
        var text = PlaybackSessionCodec.Encode(Reading());

        Assert.True(PlaybackSessionCodec.TryDecode(text, out var back));
        Assert.Equal(Reading(), back);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{}")]
    [InlineData("{\"package\":\"com.example.streamer\",\"positionMs\":1,\"durationMs\":2,\"state\":\"flying\"}")]
    [InlineData("{\"package\":\"com.example.streamer\",\"positionMs\":-5,\"durationMs\":2,\"state\":\"playing\"}")]
    [InlineData("{\"package\":\"com.example.streamer\",\"positionMs\":\"soon\",\"durationMs\":2,\"state\":\"playing\"}")]
    [InlineData("{\"package\":\"not a package\",\"positionMs\":1,\"durationMs\":2,\"state\":\"playing\"}")]
    public void GarbledOrIncompleteTextIsIgnored(string? text)
    {
        Assert.False(PlaybackSessionCodec.TryDecode(text, out _));
    }

    [Fact]
    public void ASessionOfAPackagePulpDidNotNameIsNeverRelayed()
    {
        var relay = new PlaybackSessionRelay();

        Assert.Null(relay.Offer(Reading(package: Other), Named));
    }

    [Fact]
    public void NothingNamedMeansNothingRelayed()
    {
        var relay = new PlaybackSessionRelay();

        Assert.Null(relay.Offer(Reading(), HiddenApps.None));
    }

    [Fact]
    public void TheTitleTravelsOnlyForANamedPackage()
    {
        var relay = new PlaybackSessionRelay();

        var sent = relay.Offer(Reading(title: "  Harbor Lights  "), Named);

        Assert.Equal("Harbor Lights", sent!.Title);
    }

    [Fact]
    public void ABlankTitleIsLeftOut()
    {
        var relay = new PlaybackSessionRelay();

        Assert.Null(relay.Offer(Reading(title: "   "), Named)!.Title);
    }

    [Fact]
    public void ALongTitleIsCut()
    {
        var relay = new PlaybackSessionRelay();

        var sent = relay.Offer(Reading(title: new string('x', 900)), Named);

        Assert.Equal(PlaybackSessionHandoff.MaxTitleLength, sent!.Title!.Length);
    }

    [Fact]
    public void TheFirstReadingIsSentAndAnIdenticalOneIsNot()
    {
        var relay = new PlaybackSessionRelay();

        Assert.NotNull(relay.Offer(Reading(), Named));
        Assert.Null(relay.Offer(Reading(), Named));
    }

    [Fact]
    public void AnOrdinaryTickOfPositionIsNotSent()
    {
        var relay = new PlaybackSessionRelay();
        relay.Offer(Reading(positionMs: 60000), Named);

        Assert.Null(relay.Offer(Reading(positionMs: 61000), Named));
        Assert.Null(relay.Offer(Reading(positionMs: 60000 + PlaybackSessionHandoff.ProgressStepMs - 1), Named));
    }

    [Fact]
    public void AStepOfProgressIsSentAndBecomesTheNewBaseline()
    {
        var relay = new PlaybackSessionRelay();
        relay.Offer(Reading(positionMs: 60000), Named);
        var stepped = 60000 + PlaybackSessionHandoff.ProgressStepMs;

        Assert.NotNull(relay.Offer(Reading(positionMs: stepped), Named));
        Assert.Null(relay.Offer(Reading(positionMs: stepped + 1000), Named));
    }

    [Fact]
    public void ASeekBackwardsPastTheStepIsSent()
    {
        var relay = new PlaybackSessionRelay();
        relay.Offer(Reading(positionMs: 600000), Named);

        Assert.NotNull(relay.Offer(Reading(positionMs: 60000), Named));
    }

    [Fact]
    public void AChangeOfStateIsSentWhateverThePosition()
    {
        var relay = new PlaybackSessionRelay();
        relay.Offer(Reading(), Named);

        Assert.NotNull(relay.Offer(Reading(state: PlaybackSessionState.Paused), Named));
        Assert.Null(relay.Offer(Reading(state: PlaybackSessionState.Paused), Named));
        Assert.NotNull(relay.Offer(Reading(state: PlaybackSessionState.Playing), Named));
    }

    [Fact]
    public void AChangeOfTitleIsSent()
    {
        var relay = new PlaybackSessionRelay();
        relay.Offer(Reading(), Named);

        Assert.NotNull(relay.Offer(Reading(title: "Harbor Lights, Part Two"), Named));
    }

    [Fact]
    public void ADurationThatArrivesLateIsSent()
    {
        var relay = new PlaybackSessionRelay();
        relay.Offer(Reading(durationMs: 0), Named);

        Assert.NotNull(relay.Offer(Reading(durationMs: 2640000), Named));
    }

    [Fact]
    public void PackagesAreThrottledSeparately()
    {
        var relay = new PlaybackSessionRelay();
        var both = HiddenApps.Of([Streamer, "com.example.otherstreamer"]);
        relay.Offer(Reading(), both);

        Assert.NotNull(relay.Offer(Reading(package: "com.example.otherstreamer"), both));
        Assert.Null(relay.Offer(Reading(), both));
    }

    [Fact]
    public void ForgettingAPackageLetsItsNextSessionThrough()
    {
        var relay = new PlaybackSessionRelay();
        relay.Offer(Reading(), Named);
        relay.Forget(Streamer);

        Assert.NotNull(relay.Offer(Reading(), Named));
    }

    [Fact]
    public void ACallerMayNotWriteAnOwnerPackageIntoTheNamedSet()
    {
        // HiddenApps refuses the owner's own namespace, and the relay inherits that.
        var relay = new PlaybackSessionRelay();
        var owner = HiddenApps.Of(["com.storyworthtelling.focusphone.communication"]);

        Assert.Null(relay.Offer(Reading(package: "com.storyworthtelling.focusphone.communication"), owner));
    }

    [Fact]
    public void ClosingAfterAStopWasAlreadySentSendsNothingMore()
    {
        var relay = new PlaybackSessionRelay();
        relay.Offer(Reading(positionMs: 900000, state: PlaybackSessionState.Stopped), Named);

        Assert.Null(relay.Close(Reading(positionMs: 0, state: PlaybackSessionState.Stopped), Named));
    }

    [Fact]
    public void ClosingWithNoStateCarriesTheLastPositionTitleAndDuration()
    {
        var relay = new PlaybackSessionRelay();
        relay.Offer(Reading(positionMs: 900000, state: PlaybackSessionState.Playing), Named);

        var closing = relay.Close(Reading(title: null, positionMs: 0, durationMs: 0, state: PlaybackSessionState.Stopped), Named);

        Assert.NotNull(closing);
        Assert.Equal(PlaybackSessionState.Stopped, closing!.State);
        Assert.Equal(900000, closing.PositionMs);
        Assert.Equal("Harbor Lights", closing.Title);
        Assert.Equal(2640000, closing.DurationMs);
    }

    [Fact]
    public void ClosingKeepsItsOwnPositionWhenItHasOne()
    {
        var relay = new PlaybackSessionRelay();
        relay.Offer(Reading(positionMs: 900000), Named);

        var closing = relay.Close(Reading(positionMs: 950000, state: PlaybackSessionState.Stopped), Named);

        Assert.Equal(950000, closing!.PositionMs);
    }

    [Fact]
    public void ClosingForgetsThePackageSoTheNextSessionIsSent()
    {
        var relay = new PlaybackSessionRelay();
        relay.Offer(Reading(), Named);
        relay.Close(Reading(state: PlaybackSessionState.Stopped), Named);

        Assert.NotNull(relay.Offer(Reading(), Named));
    }

    [Fact]
    public void ClosingAnUnnamedPackageSendsNothing()
    {
        Assert.Null(new PlaybackSessionRelay().Close(Reading(package: Other, state: PlaybackSessionState.Stopped), Named));
    }

    [Fact]
    public void ForgettingEverythingLetsAStillPlayingSessionBeSentAgain()
    {
        var relay = new PlaybackSessionRelay();
        relay.Offer(Reading(), Named);
        relay.ForgetAll();

        Assert.NotNull(relay.Offer(Reading(), Named));
    }

    [Theory]
    [InlineData(false, true, false, SessionWatch.Register)]
    [InlineData(true, true, true, SessionWatch.Keep)]
    [InlineData(true, true, false, SessionWatch.Replace)]
    [InlineData(true, false, false, SessionWatch.Close)]
    [InlineData(false, false, false, SessionWatch.Keep)]
    public void TheWatcherDecidesFromTheSessionTokenNotThePackage(bool watched, bool live, bool sameToken, SessionWatch expected)
    {
        Assert.Equal(expected, PlaybackSessionWatch.Decide(watched, live, sameToken));
    }
}
