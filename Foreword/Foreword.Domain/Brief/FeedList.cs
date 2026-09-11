namespace Foreword.Domain.Brief;

/// <summary>
/// One source the brief draws from (T5).
/// </summary>
public sealed record Feed
{
    public required string Name { get; init; }
    public required string Url { get; init; }

    /// <summary>
    /// How many articles this feed may contribute in a morning. Its <b>own</b> cap rather than a
    /// share of an overall one, so a noisy feed cannot crowd out a quiet one and a quiet morning
    /// still prints everything. Zero is muted — which is the owner's decision, not a failure.
    /// </summary>
    public int Cap { get; init; } = 5;

    /// <summary>
    /// Where this feed was guessed from, when it was guessed rather than added. A calendar named
    /// after a team is a good guess, not a decision — so it waits to be followed.
    /// </summary>
    public string? Proposed { get; init; }

    /// <summary>The morning it started failing, or null when it is fine.</summary>
    public DateOnly? FailingSince { get; init; }

    /// <summary>What it said when it failed — a status code, a timeout.</summary>
    public string LastFailure { get; init; } = string.Empty;

    /// <summary>A proposal is not a subscription until the owner says so.</summary>
    public bool IsActive => Proposed is null;

    public Feed Followed() => this with { Proposed = null };

    /// <summary>
    /// The line under the name. A failing feed says <b>what</b> and <b>for how long</b>, because
    /// that is a sentence the owner can act on — where a feed that has simply gone quiet is one
    /// they will not notice for a month.
    /// </summary>
    public string Status(DateOnly today)
    {
        if (Proposed is { Length: > 0 } proposed)
        {
            return $"Proposed from {proposed}";
        }

        if (FailingSince is { } since)
        {
            var mornings = today.DayNumber - since.DayNumber + 1;
            var howLong = mornings == 1 ? "one morning" : $"{Words(mornings)} mornings running";
            return $"Refused the fetch — {LastFailure}, {howLong}";
        }

        return Cap == 0 ? "RSS · muted" : $"RSS · cap {Cap} a morning";
    }

    // Spelled out to about a fortnight, which is as long as anyone reads as a duration rather than
    // as a number. Past that the digits carry it better.
    private static string Words(int count) => count switch
    {
        2 => "two", 3 => "three", 4 => "four", 5 => "five", 6 => "six", 7 => "seven",
        8 => "eight", 9 => "nine", 10 => "ten", 11 => "eleven", 12 => "twelve",
        13 => "thirteen", 14 => "fourteen",
        _ => count.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// The feed list, and the rule that carries the whole screen.
///
/// <para><b>A feed is never unsubscribed on the owner's behalf, however long it fails.</b> It stays
/// on the list, still marked, until they remove it — because a feed disappearing on its own is
/// indistinguishable from a feed you forgot you added, and the owner would spend a morning
/// wondering which. The Trentonian is the real case: bot-blocked at the CDN, six mornings running,
/// and the reason local news was dropped rather than the reason a row vanished.</para>
/// </summary>
public static class FeedList
{
    /// <summary>
    /// Which feeds to try. A failing one is <b>still tried</b> — quietly giving up on it would be
    /// the same unsubscribe this list refuses to make, only harder to notice. A muted one is not,
    /// because the owner said so.
    /// </summary>
    public static IReadOnlyList<Feed> ToFetch(IReadOnlyList<Feed> feeds) =>
        [.. feeds.Where(feed => feed.IsActive && feed.Cap > 0)];

    /// <summary>
    /// The list after a morning's fetching. Nothing is removed, ever; a feed that failed is marked
    /// and a feed that recovered is cleaned — the failure was recorded so the owner could act on
    /// it, and once it stops failing there is nothing to act on.
    /// </summary>
    public static IReadOnlyList<Feed> AfterAMorning(
        IReadOnlyList<Feed> feeds, IReadOnlyCollection<string> failed, DateOnly today) =>
    [
        .. feeds.Select(feed =>
        {
            if (!failed.Contains(feed.Url))
            {
                return feed.FailingSince is null ? feed : feed with { FailingSince = null, LastFailure = string.Empty };
            }

            // The first failure records the date and later ones leave it alone: the interesting
            // number is how long it has been broken, not when it last was.
            return feed.FailingSince is null ? feed with { FailingSince = today } : feed;
        }),
    ];
}
