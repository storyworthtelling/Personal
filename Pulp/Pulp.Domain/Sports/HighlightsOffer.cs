using System.Globalization;

namespace Pulp.Domain.Sports;

/// <summary>
/// A search for one game's highlights: the league's official channel (D71), the teams and the day. Names go
/// into the search phrase only; nothing from a result's title is ever read back.
/// </summary>
public sealed record HighlightsRequest(LeagueId League, string ChannelId, string Home, string Away, DateTimeOffset PlayedOn);

/// <summary>
/// Finds a game's highlight video (D313). It returns the video id and nothing else: a video's title or
/// thumbnail can say who won, so the type has no place to carry one. Null when there is none to play.
/// </summary>
public interface IHighlightsSource
{
    Task<string?> FindVideoIdAsync(HighlightsRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The official YouTube channel of each league that has one on record (D71). A league with no entry is never
/// offered highlights, so the table failing to know a channel fails closed. Channel ids are public data;
/// they have not been confirmed against the live API, and a wrong id only finds nothing.
/// </summary>
public sealed class HighlightChannels
{
    private readonly Dictionary<LeagueId, string> channels;

    public HighlightChannels(IReadOnlyDictionary<LeagueId, string> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        this.channels = new Dictionary<LeagueId, string>(channels);
    }

    public static HighlightChannels Default { get; } = new(new Dictionary<LeagueId, string>
    {
        [new LeagueId("nfl")] = "UCDVYQ4Zhbm3S2dlz7P1GBDg",
        [new LeagueId("mlb")] = "UCoLrcjPV5PbUrUyXq5mjc_A",
        [new LeagueId("nba")] = "UCWJ2lWNubArHWmf3FIHbfcQ",
        [new LeagueId("nhl")] = "UCqFMzb-4AUf6WAIbl132QKA",
        [new LeagueId("mls")] = "UCSZbXT5TLLW_i-5W8FZpFsg",
        [new LeagueId("nwsl")] = "UCL4xu08EDu0ZFZsBJUB0chw",
    });

    public bool Has(LeagueId league) => channels.ContainsKey(league);

    public string? ChannelOf(LeagueId league) => channels.GetValueOrDefault(league);
}

/// <summary>
/// When a game's highlights are offered (D313, D314): the league is open and not on a peek, the league has an
/// official channel on record, and the game is over. The offer is a quiet line on the row; nothing is looked up
/// until the viewer taps it.
/// </summary>
public static class HighlightsOffer
{
    /// <summary>How long after the start a game counts as over. Un-evidenced: long enough for any league in the table, short enough to show soon after.</summary>
    public static readonly TimeSpan GameLength = TimeSpan.FromHours(4);

    public static bool For(OpenLeague league, GameLine game, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(league);
        ArgumentNullException.ThrowIfNull(game);
        return Allowed(league) && IsOver(game.StartsAt, now);
    }

    /// <summary>The league-level half: open (no hold, no peek) and with an official channel.</summary>
    public static bool Allowed(OpenLeague league)
    {
        ArgumentNullException.ThrowIfNull(league);
        return !league.IsPeek && HighlightChannels.Default.Has(league.Line.Id);
    }

    public static bool IsOver(DateTimeOffset startsAt, DateTimeOffset now) => now - startsAt >= GameLength;

    /// <summary>"Highlights · Foxes at Gulls · Sat Jan 17": the generic label, never the video's own title.</summary>
    public static string Label(string title, DateTimeOffset startsAt, TimeZoneInfo zone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(startsAt, zone);
        return "Highlights · " + title + " · " + local.ToString("ddd MMM d", CultureInfo.InvariantCulture);
    }
}
