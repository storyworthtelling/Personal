namespace Pulp.Domain.Library;

/// <summary>
/// The lines under a Recently watched row's title (mockup 05): <see cref="Subtitle"/> ("Last: S1 · E2 ·
/// Slack Water", or "Film"); <see cref="Detail"/>, the quiet line that says how it ended and where;
/// and, for a show still in progress, the mono <see cref="ProgressLine"/> under the pencil
/// <see cref="Bar"/> across the whole show (D227).
/// </summary>
public sealed record RecentRowLines(string Subtitle, string? Detail, string? ProgressLine, ProgressMark? Bar);

/// <summary>
/// What a Recently watched row says (mockup 05, D227). A show names its last episode and, until it is
/// complete, carries the whole-show mark ("2 of 14 episodes · Northstar+"); a finished show reads
/// "Finished". A film says how it ended — Finished, Stopped at N of M min, or a mark by hand (D278) —
/// then where it played; a mark by hand was not played anywhere, so it names no place.
/// </summary>
public static class RecentRowText
{
    private const string Dot = " · ";

    public static RecentRowLines For(RecentRow row, string? episodeName, string? serviceName)
    {
        ArgumentNullException.ThrowIfNull(row);
        var place = string.IsNullOrWhiteSpace(serviceName) ? null : serviceName.Trim();

        return row.Kind == Core.TitleKind.Film ? Film(row, place) : Show(row, episodeName, place);
    }

    private static RecentRowLines Film(RecentRow row, string? place)
    {
        var detail = row.Ending switch
        {
            FilmEnding.MarkedWatched => "Marked watched by you",
            FilmEnding.MarkedUnwatched => "Marked unwatched by you",
            FilmEnding.Stopped => Where(
                row.Progress is { Unit: ProgressUnit.Minute } stop ? $"Stopped at {stop.At} of {stop.Of} min" : "Stopped partway",
                place),
            FilmEnding.Finished => Where("Finished", place),
            null => throw new ArgumentException($"The film row for '{row.Title}' carries no ending; Recently watched always knows one.", nameof(row)),
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.Ending, "A film ending with no words decided."),
        };

        return new RecentRowLines("Film", detail, ProgressLine: null, Bar: null);
    }

    private static RecentRowLines Show(RecentRow row, string? episodeName, string? place)
    {
        var subtitle = row.LastEpisode is { } last
            ? "Last: S" + last.Season + Dot + "E" + last.Number + (string.IsNullOrWhiteSpace(episodeName) ? string.Empty : Dot + episodeName.Trim())
            : "Show";

        if (row.ShowFinished)
        {
            return new RecentRowLines(subtitle, Where("Finished", place), ProgressLine: null, Bar: null);
        }

        if (row.Progress is { Unit: ProgressUnit.Episode } mark)
        {
            return new RecentRowLines(subtitle, Detail: null, Where($"{mark.At} of {mark.Of} episodes", place), mark);
        }

        // The show's length is not known yet, so there is nothing to mark against.
        return new RecentRowLines(subtitle, place, ProgressLine: null, Bar: null);
    }

    private static string Where(string what, string? place) => place is null ? what : what + Dot + place;
}
