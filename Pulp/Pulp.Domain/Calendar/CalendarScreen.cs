using System.Globalization;
using Pulp.Domain.Core;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Calendar;

/// <summary>
/// One line on the Calendar page (mockup 8): a mono time or "All day" (D148), a title line, a quiet
/// kind-and-service line, and whose it is at the end (D147). A row opens its title page, or — for a game —
/// its league on Sports; exactly one of the two is set.
/// </summary>
public sealed record CalendarScreenRow(
    string Key,
    string TimeText,
    string Title,
    string Detail,
    string Whose,
    TitleId? OpenTitle,
    LeagueId? OpenLeague);

/// <summary>One day's group, under its relative heading.</summary>
public sealed record CalendarScreenDay(string Label, IReadOnlyList<CalendarScreenRow> Rows);

/// <summary>The page, ready to draw. It carries no action beyond opening a row (D246); the area adds its own "Add a plan" line (D305).</summary>
public sealed record CalendarScreenPage(IReadOnlyList<CalendarScreenDay> Days)
{
    public bool IsEmpty => Days.Count == 0;
}

/// <summary>
/// What the page needs from the rest of Pulp to put words on an item: this viewer (named first), how a
/// viewer and a service are named, and what kind of title an id is (the item itself does not say).
/// <paramref name="ServiceName"/> answers null for a service the catalogue does not name — the line then
/// leaves it out rather than print an id.
/// </summary>
public sealed record CalendarScreenContext(
    ViewerId Viewer,
    Func<ViewerId, string> NameOf,
    Func<ServiceId, string?> ServiceName,
    Func<TitleId, TitleKind?> KindOf);

/// <summary>Turns the viewer's calendar items into the lines mockup 8 draws.</summary>
public static class CalendarScreen
{
    /// <summary>The status line under the header (mockup 8).</summary>
    public const string StatusLine = "Next two weeks · yours and shared";

    /// <summary>The status sentence while the page is read; loading is a sentence, never a spinner (§6).</summary>
    public const string ReadingStatus = "Looking at what is coming.";

    /// <summary>The status sentence when the page could not be read; Diagnostics has why.</summary>
    public const string UnreadableStatus = "The calendar could not be read just now.";

    public const string AllDayText = "All day";

    private const string Separator = " · ";

    public static CalendarScreenPage Build(
        IReadOnlyList<CalendarItem> items, CalendarScreenContext context, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(zone);

        return new CalendarScreenPage(CalendarItems.ByDay(items)
            .Select(day => new CalendarScreenDay(
                CalendarScreenDayLabel.For(day, now, zone),
                day.Items.Select(item => Row(item, context)).ToList()))
            .ToList());
    }

    public static string TimeText(CalendarWhen when)
    {
        if (when.Time is not { } time)
        {
            return AllDayText;
        }

        var hour = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
        return hour.ToString(CultureInfo.InvariantCulture) + ":" + time.Minute.ToString("00", CultureInfo.InvariantCulture)
            + (time.Hour < 12 ? " am" : " pm");
    }

    private static CalendarScreenRow Row(CalendarItem item, CalendarScreenContext context) => new(
        item.Key,
        TimeText(item.When),
        TitleLine(item),
        DetailLine(item, context),
        WhoseLine(item, context),
        item.Kind == CalendarItemKind.Game ? null : item.Title,
        item.Kind == CalendarItemKind.Game ? item.League : null);

    private static string TitleLine(CalendarItem item) => item.Kind switch
    {
        CalendarItemKind.NewEpisode when !string.IsNullOrWhiteSpace(item.Detail) => item.Name + Separator + item.Detail,
        CalendarItemKind.Plan => "Plan" + Separator + item.Name,
        _ => item.Name,
    };

    private static string DetailLine(CalendarItem item, CalendarScreenContext context)
    {
        var service = ServiceNameOf(item, context);
        var kind = item.Title is { } title ? context.KindOf(title) : null;
        return item.Kind switch
        {
            CalendarItemKind.NewEpisode => Join("New episode", service),
            CalendarItemKind.Release => Join(kind switch
            {
                TitleKind.Film => "Film release",
                TitleKind.Show => "Season premiere",
                _ => "Release",
            }, service),
            CalendarItemKind.Game => Join("Game", item.Detail),
            _ when item.Title is null => Join("Plan", item.Detail),
            _ => Join(kind switch { TitleKind.Film => "Film", TitleKind.Show => "Show", _ => null }, service, item.Detail) is { Length: > 0 } line
                ? line
                : "Plan to watch",
        };
    }

    private static string? ServiceNameOf(CalendarItem item, CalendarScreenContext context) =>
        item.Service is { } service && service != CalendarScreenSources.NoService ? context.ServiceName(service) : null;

    /// <summary>A playlist release names the playlist (D146); anything else names its people, this viewer first (D147).</summary>
    private static string WhoseLine(CalendarItem item, CalendarScreenContext context)
    {
        if (item.Playlists.Count > 0)
        {
            return string.Join(", ", item.Playlists);
        }

        return string.Join(", ", item.Whose
            .OrderBy(who => who == context.Viewer ? 0 : 1)
            .ThenBy(who => context.NameOf(who), StringComparer.OrdinalIgnoreCase)
            .Select(context.NameOf));
    }

    private static string Join(params string?[] parts) =>
        string.Join(Separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}
