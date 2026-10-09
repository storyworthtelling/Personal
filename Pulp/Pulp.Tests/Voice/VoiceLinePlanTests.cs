using Pulp.Domain.Voice;

namespace Pulp.Tests.Voice;

/// <summary>
/// When a seat's line is composed again: at most once per seat, facts and day (Index's
/// <c>VoiceSeatComposer</c>, Folio's <c>VoicedLinePlan</c>), and never when there is no profile or key.
/// </summary>
public class VoiceLinePlanTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly VoiceFacts Playlist = VoiceFactBuilder.Playlist("Rainy Sundays", entries: 6, watched: 2);

    [Fact]
    public void NothingRememberedIsComposed() =>
        Assert.Equal(VoiceLineAction.Compose, VoiceLinePlan.Decide(Playlist, voiceReady: true, remembered: null, Today));

    [Fact]
    public void TheSameSeatFactsAndDayReuseTheLine()
    {
        var remembered = new RememberedLine(Playlist.Seat, Playlist.Hash, Today, "A line already written.");
        Assert.Equal(VoiceLineAction.Reuse, VoiceLinePlan.Decide(Playlist, true, remembered, Today));
    }

    [Fact]
    public void ANewDayComposesAgain()
    {
        var remembered = new RememberedLine(Playlist.Seat, Playlist.Hash, Today.AddDays(-1), "Yesterday's line.");
        Assert.Equal(VoiceLineAction.Compose, VoiceLinePlan.Decide(Playlist, true, remembered, Today));
    }

    [Fact]
    public void ChangedFactsComposeAgain()
    {
        var remembered = new RememberedLine(Playlist.Seat, "0000000000000000", Today, "About older facts.");
        Assert.Equal(VoiceLineAction.Compose, VoiceLinePlan.Decide(Playlist, true, remembered, Today));
    }

    [Fact]
    public void AnotherSeatsLineIsNeverReused()
    {
        var greeting = VoiceFactBuilder.Greeting(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero), true, 1, 0);
        var remembered = new RememberedLine(greeting.Seat, Playlist.Hash, Today, "The greeting's line.");
        Assert.Equal(VoiceLineAction.Compose, VoiceLinePlan.Decide(Playlist, true, remembered, Today));
    }

    /// <summary>
    /// An attempt that came back with nothing (no profile, a failed call) is not retried the same day for
    /// the same facts — that is what "at most once" means — so the seat falls back instead.
    /// </summary>
    [Fact]
    public void AnAttemptThatCameBackEmptyIsNotRetriedTheSameDay()
    {
        var remembered = new RememberedLine(Playlist.Seat, Playlist.Hash, Today, Line: null);
        Assert.Equal(VoiceLineAction.Hide, VoiceLinePlan.Decide(Playlist, true, remembered, Today));

        var empty = VoiceFactBuilder.EmptyPlaylists();
        var emptyRemembered = new RememberedLine(empty.Seat, empty.Hash, Today, Line: "  ");
        Assert.Equal(VoiceLineAction.ShowNeutral, VoiceLinePlan.Decide(empty, true, emptyRemembered, Today));
    }

    [Fact]
    public void WithNoProfileOrKeyEachSeatFallsBackByItsOwnRule()
    {
        var count = 0;
        foreach (var occasion in VoicedOccasions.All)
        {
            var facts = new VoiceFacts(occasion.Seat, ["Something true."], null);
            var expected = occasion.Fallback == VoiceFallback.Hide ? VoiceLineAction.Hide : VoiceLineAction.ShowNeutral;
            Assert.Equal(expected, VoiceLinePlan.Decide(facts, voiceReady: false, remembered: null, Today));
            count++;
        }

        Assert.Equal(13, count);
    }

    [Fact]
    public void AHiddenSeatWithNoFactsIsHiddenButAnEmptyPageStillSpeaks()
    {
        Assert.Equal(VoiceLineAction.Hide,
            VoiceLinePlan.Decide(new VoiceFacts(VoiceSeat.PlaylistLine, [], null), true, null, Today));
        Assert.Equal(VoiceLineAction.Compose,
            VoiceLinePlan.Decide(VoiceFactBuilder.EmptyRecentlyWatched(), true, null, Today));
    }
}
