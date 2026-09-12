using Foreword.Domain.Prayer;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// What <c>From before</c> puts back on the page, and — the load-bearing half — <b>how it chooses</b>.
///
/// <para>It selects purely by date: yesterday, a week ago, and this date in each past year. Nothing
/// reads the journal to decide what is worth resurfacing. That is what lets the page have a memory
/// without anything interpreting it, and it is the reason the archive can stay private: a date is
/// not a reading.</para>
/// </summary>
public class ResurfacingTests
{
    private static readonly DateOnly Today = new(2026, 9, 6);

    private static JournalEntry On(int year, int month, int day, string text) =>
        new() { Date = new DateOnly(year, month, day), Text = PrayerText.Of(text) };

    [Fact]
    public void YesterdayAWeekAgoAndThisDateInEachPastYear()
    {
        var archive = new[]
        {
            On(2026, 9, 5, "yesterday"),
            On(2026, 8, 30, "a week ago"),
            On(2025, 9, 6, "last year"),
            On(2024, 9, 6, "two years ago"),
        };

        var surfaced = Resurfacing.From(archive, Today);

        Assert.Equal(
            ["Yesterday", "A week ago", "This date, 2025", "This date, 2024"],
            surfaced.Select(entry => entry.When));
    }

    /// <summary>Nothing written on those days means nothing under the heading, not an empty row.</summary>
    [Fact]
    public void ADayNothingWasWrittenOnSurfacesNothing()
    {
        var surfaced = Resurfacing.From([On(2026, 8, 14, "a random Friday")], Today);

        Assert.Empty(surfaced);
    }

    /// <summary>
    /// Two entries written on the same day both come back under the one label, in the order they
    /// were written. Collapsing them to the first would quietly drop half of a morning.
    /// </summary>
    [Fact]
    public void TwoEntriesOnTheSameDayBothSurface()
    {
        var archive = new[]
        {
            On(2026, 9, 5, "first thing"),
            On(2026, 9, 5, "and another"),
        };

        var surfaced = Resurfacing.From(archive, Today);

        Assert.Equal(2, surfaced.Count);
        Assert.All(surfaced, entry => Assert.Equal("Yesterday", entry.When));
        Assert.Equal("first thing", surfaced[0].Text.ReadBy(new Reader()));
        Assert.Equal("and another", surfaced[1].Text.ReadBy(new Reader()));
    }

    /// <summary>
    /// The 29th of February is the one date that is not in most years. A leap-day journal entry
    /// would otherwise be unreachable on three mornings out of four — and, worse, the naive
    /// construction of "this date last year" throws rather than returning nothing.
    /// </summary>
    [Fact]
    public void TheTwentyNinthOfFebruaryDoesNotThrowOnANonLeapYear()
    {
        var leapDay = new DateOnly(2028, 2, 29);
        var archive = new[] { On(2027, 2, 28, "the year before") };

        var surfaced = Resurfacing.From(archive, leapDay);

        Assert.Empty(surfaced);
    }

    /// <summary>
    /// Each entry in the archive is asked once which window it belongs to, so the same sentence
    /// written on two qualifying days surfaces under both labels rather than being collapsed. The
    /// archive is the record of what was written; deciding two identical mornings were one morning
    /// would be the page inventing.
    /// </summary>
    [Fact]
    public void TheSameWordsOnTwoQualifyingDaysSurfaceUnderBoth()
    {
        var archive = new[]
        {
            On(2026, 9, 5, "the same thing again"),
            On(2025, 9, 6, "the same thing again"),
        };

        var surfaced = Resurfacing.From(archive, Today);

        Assert.Equal(["Yesterday", "This date, 2025"], surfaced.Select(entry => entry.When));
    }

    /// <summary>
    /// Past years go back as far as the archive does, newest first, so the oldest entry is at the
    /// bottom where a reader expects it.
    /// </summary>
    [Fact]
    public void PastYearsRunNewestFirst()
    {
        var archive = new[]
        {
            On(2022, 9, 6, "oldest"),
            On(2025, 9, 6, "newest"),
            On(2023, 9, 6, "middle"),
        };

        var surfaced = Resurfacing.From(archive, Today);

        Assert.Equal(
            ["This date, 2025", "This date, 2023", "This date, 2022"],
            surfaced.Select(entry => entry.When));
    }

    private sealed class Reader : IPrayerReader
    {
    }
}
