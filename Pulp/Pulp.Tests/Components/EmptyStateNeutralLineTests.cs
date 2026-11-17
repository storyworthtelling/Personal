using Pulp.Domain.Voice;

namespace Pulp.Tests.Components;

/// <summary>
/// Every empty page is voiced, each with a neutral Serif italic fallback (D264, mockup 13). The fallbacks are
/// written on the suite's voice-line Call Sheet (audit W10.3) beside the line they stand in for, and
/// <c>Pulp.Android/Components/EmptyState.cs</c> reads them from there.
///
/// <para>The bug it reintroduces: a new empty-page seat with no neutral line, so on a phone with no profile or
/// key the page is simply blank, or the head throws on first paint.</para>
/// </summary>
public class EmptyStateNeutralLineTests
{
    [Fact]
    public void EveryNeutralLineSeatHasALine()
    {
        var seats = VoicedOccasions.All.Where(occasion => occasion.Fallback == VoiceFallback.NeutralLine).ToList();
        Assert.True(seats.Count >= 8,
            $"Expected the eight empty-page seats of D264 and D112; VoicedOccasions gave {seats.Count}.");
        Assert.All(seats, occasion => Assert.False(string.IsNullOrWhiteSpace(occasion.Line.Neutral), occasion.Seat.ToString()));
    }

    [Fact]
    public void NoNeutralLineIsWrittenForASeatThatHidesInstead()
    {
        Assert.All(VoicedOccasions.All.Where(occasion => occasion.Fallback == VoiceFallback.Hide),
            occasion => Assert.Null(occasion.Line.Neutral));
    }

    [Fact]
    public void TheDrawnLinesAreTheDrawnLines()
    {
        // Mockup 13 draws these two word for word.
        Assert.Equal("Nothing started yet. Discover has this week’s stack.",
            VoicedOccasions.For(VoiceSeat.EmptyContinueWatching).Line.Neutral);
        Assert.Equal("That’s the week. More on Friday.", VoicedOccasions.For(VoiceSeat.StackUsedUp).Line.Neutral);
    }

    [Fact]
    public void TheHeadReadsItsLinesFromTheSheet()
    {
        var source = File.ReadAllText(Path.Combine(PulpSource.Root(), "Pulp.Android/Components/EmptyState.cs"));

        Assert.Contains("VoicedOccasions.For(seat).Line.Neutral", source);
    }
}
