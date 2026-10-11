using System.Globalization;

namespace Pulp.Domain.Sports;

/// <summary>
/// The words on a Sports row (mockup 07), decided here so the head only paints them. Time is 12-hour in
/// the viewer's own zone (§11); a day is relative to the viewer's today, through
/// <see cref="Library.RecentDayLabel"/> for the past and today and the same grammar looking forward.
/// Nothing here is handed anything about how a game went, so nothing here can say.
/// </summary>
public static class SportsScreenText
{
    /// <summary>Days ahead that still read as a weekday name ("Friday") rather than a date.</summary>
    private const int WeekdayWindow = 6;

    /// <summary>"7:30 pm": lowercase meridiem, no leading zero, in <paramref name="zone"/>.</summary>
    public static string Clock12(DateTimeOffset moment, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(moment, zone);
        return local.ToString("h:mm", CultureInfo.InvariantCulture) + (local.Hour < 12 ? " am" : " pm");
    }

    /// <summary>
    /// "Today", "Tonight", "Tomorrow", a weekday within the week, otherwise "Jan 27" (the year only when
    /// it is not this one); and for the past "Yesterday", a weekday, a date.
    /// </summary>
    public static string DayLabel(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        if (day <= today)
        {
            return Library.RecentDayLabel.For(day, at, now, zone);
        }

        var ahead = day.DayNumber - today.DayNumber;
        if (ahead == 1)
        {
            return "Tomorrow";
        }

        return ahead <= WeekdayWindow
            ? day.ToString("dddd", CultureInfo.InvariantCulture)
            : ShortDate(day, today);
    }

    /// <summary>
    /// A game's second line: "Sunday · recorded · from the start" for an unwatched recording (D55),
    /// "Sunday · 7:30 pm" for a next game.
    /// </summary>
    public static string GameDetail(GameLine game, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(game);
        var day = DayLabel(game.StartsAt, now, zone);
        return game.Recorded
            ? day + " · recorded · from the start"
            : day + " · " + Clock12(game.StartsAt, zone);
    }

    /// <summary>"On Gridline TV · channel 7" (D57): the row names the channel; absent when the feed gave none.</summary>
    public static string? ChannelLine(string? channel) =>
        string.IsNullOrWhiteSpace(channel) ? null : "On " + channel.Trim();

    /// <summary>
    /// What Play says while no feed hands Pulp a link to a game (D57): the channel it is on, and that
    /// Pulp cannot open it yet. A hand-off needs a service and a link; the sheet carries only a channel.
    /// </summary>
    public static string PlayNote(string? channelLine) =>
        (channelLine is null ? string.Empty : channelLine + ". ") + "Pulp can't open this game for you yet.";

    /// <summary>The chip on a game: only what is not ordinary. A championship game is "Final" as mockup 07 draws it.</summary>
    public static string? StageChip(GameStage stage) => stage switch
    {
        GameStage.Preseason => "Preseason",
        GameStage.RegularSeason => null,
        GameStage.Playoffs => "Playoffs",
        GameStage.Championship => "Final",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "A game stage with no chip decided."),
    };

    /// <summary>"Championship · Sun Feb 8".</summary>
    public static string ChampionshipDetail(DateTimeOffset startsAt, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(startsAt, zone);
        return "Championship · " + local.ToString("ddd MMM d", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A folded league's state (D241): "Preseason · next Fri", "Back Nov 8"; a league in season says so.
    /// </summary>
    public static string LeagueState(LeagueLine league, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(league);
        switch (league.Phase)
        {
            case SeasonPhase.Preseason:
                if (league.NextDate is not { } start)
                {
                    return "Preseason";
                }

                var ahead = start.DayNumber - today.DayNumber;
                return ahead is >= 0 and <= WeekdayWindow
                    ? "Preseason · next " + start.ToString("ddd", CultureInfo.InvariantCulture)
                    : "Preseason · " + ShortDate(start, today);
            case SeasonPhase.OffSeason:
                return league.NextDate is { } back ? "Back " + ShortDate(back, today) : "Off season";
            case SeasonPhase.RegularSeason:
                return "In season";
            case SeasonPhase.Postseason:
                return "Playoffs";
            default:
                throw new ArgumentOutOfRangeException(nameof(league), league.Phase, "A season phase with no state decided.");
        }
    }

    /// <summary>
    /// The letters a badge shows until its artwork arrives ("CFL", "HG"): the capitals that start the
    /// words, or a single word's first letters.
    /// </summary>
    public static string Monogram(string name, int letters)
    {
        ArgumentNullException.ThrowIfNull(name);
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var initials = words.Where(word => char.IsUpper(word[0])).Select(word => word[0]).ToList();
        if (initials.Count >= 2)
        {
            return new string(initials.Take(letters).ToArray()).ToUpperInvariant();
        }

        var first = words.FirstOrDefault() ?? string.Empty;
        return first[..Math.Min(letters, first.Length)].ToUpperInvariant();
    }

    private static string ShortDate(DateOnly day, DateOnly today) =>
        day.Year == today.Year
            ? day.ToString("MMM d", CultureInfo.InvariantCulture)
            : day.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}
