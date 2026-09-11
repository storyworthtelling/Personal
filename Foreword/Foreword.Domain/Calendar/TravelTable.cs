namespace Foreword.Domain.Calendar;

/// <summary>
/// The drives measured for one day, and the address every column's morning starts from.
///
/// <para>A table rather than a number on each event, because a drive has two ends and the columns
/// do not share them: the same field is a different drive from the office than it is from the
/// school. <see cref="TravelPlan"/> works out which legs a person's day actually needs; this only
/// answers how long one of them takes, and answers <c>null</c> for a leg nobody measured — an
/// invented length would make the day look occupied by something that was never checked.</para>
///
/// <para>Legs are keyed by the location <b>text</b>, which is what a calendar carries: typed by
/// whoever made the event, with whatever case and spacing they used. Matching it strictly is a
/// failure that shows up as nothing at all — every band silently missing, no error anywhere, and a
/// page that looks exactly like a day with no drives in it.</para>
/// </summary>
public sealed class TravelTable
{
    // Where a family's day starts when nothing has said otherwise. Not an address: the router
    // resolves the word, and Maps already holds the house under it.
    public const string DefaultHome = "Home";

    private readonly Dictionary<(string From, string To), int> legs;

    private TravelTable(string home, Dictionary<(string, string), int> legs)
    {
        Home = home;
        this.legs = legs;
    }

    /// <summary>A day nobody has measured. Every lookup is unknown; the house is still the house.</summary>
    public static TravelTable NothingMeasured { get; } = new(DefaultHome, []);

    public string Home { get; }

    public static TravelTable Of(string home, IEnumerable<(string From, string To, int Minutes)> legs)
    {
        var table = new Dictionary<(string, string), int>();
        foreach (var leg in legs)
        {
            table[(Key(leg.From), Key(leg.To))] = leg.Minutes;
        }

        return new TravelTable(string.IsNullOrWhiteSpace(home) ? DefaultHome : home.Trim(), table);
    }

    /// <summary>
    /// How long the drive takes, or null when nobody measured it. Direction matters and the return
    /// leg is never assumed: rush hour runs one way in the morning and the other at six.
    /// </summary>
    public int? Minutes(string from, string to) =>
        legs.TryGetValue((Key(from), Key(to)), out var minutes) ? minutes : null;

    private static string Key(string place) => place.Trim().ToLowerInvariant();
}
