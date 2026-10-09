namespace Pulp.Domain.Library;

/// <summary>Why a row is dimmed: a film not yet out (D32) or a caught-up show waiting on a season (D224).</summary>
public enum RowDim
{
    None,
    NotOutYet,
    CaughtUp,
}

/// <summary>
/// What the library knows about one entry, supplied by the caller. <see cref="Watched"/> is a film
/// seen or a show finished for good (D153); <see cref="CaughtUp"/> is a show with nothing left out.
/// </summary>
public sealed record EntryStanding(
    bool Watched = false,
    bool CaughtUp = false,
    int? NextSeason = null,
    DateOnly? NextSeasonOut = null,
    DateOnly? ReleasesOn = null)
{
    public static readonly EntryStanding Unknown = new();
}

/// <summary>One playlist row: struck through (D101), dimmed and until when (D32, D224).</summary>
public sealed record PlaylistRow(PlaylistEntry Entry, bool Struck, RowDim Dim, int? NextSeason, DateOnly? DimUntil);

/// <summary>
/// A playlist's rows in hand order (D103). Watched entries are struck and kept (D101), or filtered
/// when "hide watched" is on. A film not yet out dims until its date (D32); a caught-up show dims
/// until its next season lands (D224) — a show with episodes left is never dimmed by one. A struck
/// row is not also dimmed (D153). Today is an argument.
/// </summary>
public static class Playlists
{
    public static IReadOnlyList<PlaylistRow> Rows(Playlist playlist, Func<PlaylistEntry, EntryStanding> standing, DateOnly today, bool hideWatched)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(standing);

        var rows = new List<PlaylistRow>();
        foreach (var entry in playlist.Entries)
        {
            var known = standing(entry) ?? EntryStanding.Unknown;
            if (known.Watched)
            {
                if (!hideWatched)
                {
                    rows.Add(new PlaylistRow(entry, Struck: true, RowDim.None, known.NextSeason, DimUntil: null));
                }

                continue;
            }

            if (known.ReleasesOn is { } release && release > today)
            {
                rows.Add(new PlaylistRow(entry, Struck: false, RowDim.NotOutYet, known.NextSeason, release));
            }
            else if (known.CaughtUp && !(known.NextSeasonOut is { } landed && landed <= today))
            {
                rows.Add(new PlaylistRow(entry, Struck: false, RowDim.CaughtUp, known.NextSeason, known.NextSeasonOut));
            }
            else
            {
                rows.Add(new PlaylistRow(entry, Struck: false, RowDim.None, known.NextSeason, DimUntil: null));
            }
        }

        return rows;
    }
}
