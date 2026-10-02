using Foreword.Domain.Prayer;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// Which requests print, in what order, and the one rule that needs the page to remember something:
/// <b>an answered request prints struck through exactly once, and then stops printing.</b>
///
/// <para>Striking it through is how it is answered, so the strike is the acknowledgement — print it
/// never and the answer is silently swallowed; print it every morning and a list that should be
/// getting shorter grows a graveyard.</para>
/// </summary>
public class PrayerPrintingTests
{
    private static readonly DateOnly Today = new(2026, 9, 6);

    private static PrayerRequest Held(string id, int daysAgo) => new()
    {
        Id = id,
        Text = PrayerText.Of(id),
        Held = Today.AddDays(-daysAgo),
    };

    private sealed class Reader : IPrayerReader
    {
    }

    private static string Read(PrayerText text) => text.ReadBy(new Reader());

    [Fact]
    public void AHeldRequestPrintsWithHowLongItHasBeenHeld()
    {
        var printed = PrayerPrinting.ForToday([Held("dad", 9)], Today);

        var row = Assert.Single(printed.Rows);
        Assert.Equal("dad", Read(row.Text));
        Assert.Equal("held 9 days", row.Since);
        Assert.False(row.Struck);
    }

    [Fact]
    public void AnAnsweredRequestPrintsStruckThroughAndIsReportedAsShown()
    {
        var answered = Held("the team decision", 12) with { Answered = Today.AddDays(-1) };

        var printed = PrayerPrinting.ForToday([answered], Today);

        var row = Assert.Single(printed.Rows);
        Assert.True(row.Struck);
        Assert.Equal("answered", row.Since);
        Assert.Equal(["the team decision"], printed.StrikesShown);
    }

    /// <summary>
    /// The morning after. This is the whole point of <see cref="PrayerRequest.StrikePrinted"/>: the
    /// item is gone, and nothing is reported, because there is nothing left to mark.
    /// </summary>
    [Fact]
    public void OnceTheStrikeHasBeenPrintedTheRequestStopsPrinting()
    {
        var settled = Held("the team decision", 13) with
        {
            Answered = Today.AddDays(-2),
            StrikePrinted = true,
        };

        var printed = PrayerPrinting.ForToday([settled], Today);

        Assert.Empty(printed.Rows);
        Assert.Empty(printed.StrikesShown);
    }

    /// <summary>
    /// Held requests keep the order they were stored in — not sorted by how long they have been
    /// held, which would shuffle the list every morning and make it unreadable as a list — and the
    /// answered ones fall to the bottom, where a thing being taken off a list belongs.
    /// </summary>
    [Fact]
    public void AnsweredRequestsFallToTheBottomAndTheRestKeepTheirOrder()
    {
        var requests = new[]
        {
            Held("dad", 9),
            Held("the team decision", 12) with { Answered = Today },
            Held("the Kellers", 21),
            Held("Riley", 44),
        };

        var printed = PrayerPrinting.ForToday(requests, Today);

        Assert.Equal(
            ["dad", "the Kellers", "Riley", "the team decision"],
            printed.Rows.Select(row => Read(row.Text)));
    }

    /// <summary>
    /// Two answered on the same morning are both struck and both reported. Reporting one would
    /// leave the other printing struck through forever, which is the failure this rule exists to
    /// stop, arriving one item at a time.
    /// </summary>
    [Fact]
    public void EveryStrikeShownIsReported()
    {
        var requests = new[]
        {
            Held("one", 3) with { Answered = Today },
            Held("two", 4) with { Answered = Today },
        };

        var printed = PrayerPrinting.ForToday(requests, Today);

        Assert.Equal(["one", "two"], printed.StrikesShown);
    }

    [Fact]
    public void AnEmptyListIsAnEmptyPageRatherThanAnything()
    {
        var printed = PrayerPrinting.ForToday([], Today);

        Assert.Empty(printed.Rows);
        Assert.Empty(printed.StrikesShown);
    }
}
