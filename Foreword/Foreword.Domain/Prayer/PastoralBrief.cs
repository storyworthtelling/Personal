namespace Foreword.Domain.Prayer;

/// <summary>
/// What the pastoral line is written from.
///
/// <para>Shaped rather than raw: the counts and the longest hold are facts the page already knows,
/// and handing over the whole list would be sending more than the job needs. The one piece of
/// content that does go is the most recent entry, because a line that cannot see what was written
/// yesterday can only comment on arithmetic.</para>
/// </summary>
public sealed record PastoralBrief
{
    public required int Held { get; init; }

    public required int AnsweredThisMorning { get; init; }

    /// <summary>Already in words — "held 6 weeks" — so the composer is not doing date arithmetic.</summary>
    public string LongestHeld { get; init; } = string.Empty;

    public PrayerText MostRecentEntry { get; init; } = PrayerText.Empty;

    /// <summary>
    /// A morning with nothing held and nothing written is not worth a call. Composing a pastoral
    /// line about an empty list spends money to produce a sentence about nothing.
    /// </summary>
    public bool HasSomethingToSay =>
        Held > 0 || AnsweredThisMorning > 0 || !MostRecentEntry.IsEmpty;
}
