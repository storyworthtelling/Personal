namespace Foreword.Domain.Prayer;

/// <summary>What went on the page, and what the store has to remember because it did.</summary>
public readonly record struct PrintedPrayerList(
    IReadOnlyList<PrintedRequest> Rows,
    IReadOnlyList<string> StrikesShown);

/// <summary>
/// Which requests print this morning.
///
/// <para>The rule that needs remembering: <b>an answered request prints struck through exactly
/// once, and then stops printing.</b> Striking it through is how it is answered, so the strike is
/// the acknowledgement — never print it and the answer is swallowed silently; print it every
/// morning and a list that should be getting shorter grows a graveyard instead.</para>
///
/// <para>Which is why this returns the ids whose strike it showed. Drawing the page is what earns
/// the mark, so the caller writes it back afterwards rather than the page assuming it happened.</para>
/// </summary>
public static class PrayerPrinting
{
    public static PrintedPrayerList ForToday(IReadOnlyList<PrayerRequest> requests, DateOnly today)
    {
        var rows = new List<PrintedRequest>();
        var strikes = new List<string>();

        // Held requests keep the order they were stored in. Sorting by how long each has been held
        // would reshuffle the list every morning, and a list whose order moves is not a list the
        // owner can read the same way twice.
        foreach (var request in requests.Where(request => request.Answered is null))
        {
            rows.Add(new PrintedRequest(request.Text, HoldDuration.Describe(request.Held, today), Struck: false));
        }

        // The answered fall to the bottom, where something being taken off a list belongs — and
        // only the ones whose strike has not been shown yet.
        foreach (var request in requests.Where(
            request => request.Answered is not null && !request.StrikePrinted))
        {
            rows.Add(new PrintedRequest(request.Text, "answered", Struck: true));
            strikes.Add(request.Id);
        }

        return new PrintedPrayerList(rows, strikes);
    }
}
