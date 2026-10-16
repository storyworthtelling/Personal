using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;

namespace Pulp.Domain.Calendar;

/// <summary>
/// Which titles the calendar read looks up in the catalogue (D358): every show an adult has sat with, every show
/// on a playlist, and a film on a playlist only when the availability source gave it a day — a film without one
/// cannot reach the calendar, so looking it up would spend a request for nothing.
/// </summary>
public static class CalendarTitleWants
{
    public static IReadOnlyList<TitleId> Of(
        IEnumerable<TitleId> tracked,
        IEnumerable<HouseholdPlaylist> playlists,
        IEnumerable<FilmReleaseDate> filmDates)
    {
        ArgumentNullException.ThrowIfNull(tracked);
        ArgumentNullException.ThrowIfNull(playlists);
        ArgumentNullException.ThrowIfNull(filmDates);

        var dated = filmDates.Select(date => date.Title).ToHashSet();
        var entries = playlists.SelectMany(playlist => playlist.List.Entries).ToList();
        return tracked
            .Concat(entries.Where(entry => entry.Kind == PlaylistEntryKind.Show).Select(entry => new TitleId(entry.Key)))
            .Concat(entries.Where(entry => entry.Kind == PlaylistEntryKind.Film).Select(entry => new TitleId(entry.Key)).Where(dated.Contains))
            .Distinct()
            .ToList();
    }
}
