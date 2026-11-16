using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Voice;
using SeatMemory = Storyworthtelling.CallSheet.SeatMemory;
using SeatPlan = Storyworthtelling.CallSheet.SeatPlan;

namespace Pulp.Tests.Voice;

/// <summary>
/// When a seat's line is composed again: the suite's one rule (<see cref="SeatPlan"/>, audit W10.3) — at most
/// once per seat, facts and day; an empty attempt tried again after a short wait; never with no profile or key.
/// </summary>
public class VoiceLinePlanTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 18, 0, 0, TimeSpan.Zero);
    private static readonly VoiceFacts Playlist = VoiceFactBuilder.Playlist("Rainy Sundays", entries: 6, watched: 2);

    private static SeatMemory Kept(VoiceFacts facts, string? line, DateOnly? day = null, DateTimeOffset? at = null) =>
        SeatPlan.Remember(facts.Hash, day ?? Today, line, at ?? Now.AddHours(-1));

    [Fact]
    public void AFirstLookComposes()
    {
        Assert.Equal(VoiceLineAction.Compose, VoiceLinePlan.Decide(Playlist, voiceReady: true, remembered: null, Now, Today));
    }

    [Fact]
    public void TheSameSeatFactsAndDayReuseTheLine()
    {
        Assert.Equal(VoiceLineAction.Reuse,
            VoiceLinePlan.Decide(Playlist, true, Kept(Playlist, "A line already written."), Now, Today));
    }

    [Fact]
    public void ANewDayComposesAgain()
    {
        Assert.Equal(VoiceLineAction.Compose,
            VoiceLinePlan.Decide(Playlist, true, Kept(Playlist, "Yesterday's line.", Today.AddDays(-1)), Now, Today));
    }

    [Fact]
    public void ChangedFactsComposeAgain()
    {
        var older = SeatPlan.Remember("0000000000000000", Today, "About older facts.", Now.AddHours(-1));
        Assert.Equal(VoiceLineAction.Compose, VoiceLinePlan.Decide(Playlist, true, older, Now, Today));
    }

    /// <summary>
    /// An empty attempt is believed only for a short while (audit W10.3): a key restored a minute later used to wait
    /// until tomorrow. Meanwhile each seat falls back by its own rule.
    /// </summary>
    [Fact]
    public void AnEmptyAttemptFallsBackThenIsTriedAgain()
    {
        var justNow = Kept(Playlist, null, at: Now.AddMinutes(-2));
        Assert.Equal(VoiceLineAction.Hide, VoiceLinePlan.Decide(Playlist, true, justNow, Now, Today));

        var empty = VoiceFactBuilder.EmptyPlaylists();
        Assert.Equal(VoiceLineAction.ShowNeutral,
            VoiceLinePlan.Decide(empty, true, Kept(empty, "  ", at: Now.AddMinutes(-2)), Now, Today));

        var aWhileAgo = Kept(Playlist, null, at: Now - SeatPlan.RetryAfter);
        Assert.Equal(VoiceLineAction.Compose, VoiceLinePlan.Decide(Playlist, true, aWhileAgo, Now, Today));
    }

    [Fact]
    public void WithNoProfileOrKeyEachSeatFallsBackByItsOwnRule()
    {
        var count = 0;
        foreach (var occasion in VoicedOccasions.All)
        {
            var facts = new VoiceFacts(occasion.Seat, ["Something true."], null);
            var expected = occasion.Fallback == VoiceFallback.Hide ? VoiceLineAction.Hide : VoiceLineAction.ShowNeutral;
            Assert.Equal(expected, VoiceLinePlan.Decide(facts, voiceReady: false, remembered: null, Now, Today));
            count++;
        }

        Assert.Equal(15, count);
    }

    [Fact]
    public void AHiddenSeatWithNoFactsIsHiddenButAnEmptyPageStillSpeaks()
    {
        Assert.Equal(VoiceLineAction.Hide,
            VoiceLinePlan.Decide(new VoiceFacts(VoiceSeat.PlaylistLine, [], null), true, null, Now, Today));
        Assert.Equal(VoiceLineAction.Compose,
            VoiceLinePlan.Decide(VoiceFactBuilder.EmptyRecentlyWatched(), true, null, Now, Today));
    }

    /// <summary>Two children on one phone keep their own lines (audit 06: one child's line overwrote another's).</summary>
    [Fact]
    public void EachChildsSeatIsItsOwn()
    {
        var robin = new ChildRules(new ViewerId("robin"), AgeBand.UnderSeven);
        var juniper = new ChildRules(new ViewerId("juniper"), AgeBand.UnderSeven);

        Assert.Equal("robin", ChildVoiceFacts.EmptyFavorites(robin).Subject);
        Assert.Equal("juniper", ChildVoiceFacts.Greeting(juniper, Now, firstOpenOfDay: false).Subject);
        Assert.Equal(string.Empty, Playlist.Subject);
    }
}
