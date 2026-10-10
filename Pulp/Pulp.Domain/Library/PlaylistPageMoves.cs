using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;

namespace Pulp.Domain.Library;

/// <summary>One step of hand ordering (D103), made by a Move rather than a drag.</summary>
public enum PlaylistMove
{
    Up,
    Down,
    ToTop,
    ToBottom,
}

/// <summary>
/// Hand order by Move (D103, by the brief's ruling: a target big enough to hit rather than a drag —
/// Plate's precedent for its scan pages). Only the moves that change something are offered, in the
/// order a hand reaches for them, and each lands through <see cref="Playlist.Move"/>, so every other
/// entry keeps its place.
/// </summary>
public static class PlaylistPageMoves
{
    public static IReadOnlyList<PlaylistMove> Offered(Playlist playlist, PlaylistEntry entry)
    {
        var at = IndexOf(playlist, entry);
        var last = playlist.Entries.Count - 1;
        var moves = new List<PlaylistMove>();
        if (at > 0)
        {
            moves.Add(PlaylistMove.ToTop);
            moves.Add(PlaylistMove.Up);
        }

        if (at < last)
        {
            moves.Add(PlaylistMove.Down);
            moves.Add(PlaylistMove.ToBottom);
        }

        return moves;
    }

    public static Playlist Apply(Playlist playlist, PlaylistEntry entry, PlaylistMove move)
    {
        var at = IndexOf(playlist, entry);
        if (!Offered(playlist, entry).Contains(move))
        {
            throw new ArgumentException($"'{Label(move)}' does not change where this entry sits on '{playlist.Name}'.", nameof(move));
        }

        var to = move switch
        {
            PlaylistMove.Up => at - 1,
            PlaylistMove.Down => at + 1,
            PlaylistMove.ToTop => 0,
            PlaylistMove.ToBottom => playlist.Entries.Count - 1,
            _ => throw new ArgumentOutOfRangeException(nameof(move), move, "A move with no place decided."),
        };
        return playlist.Move(entry, to);
    }

    public static string Label(PlaylistMove move) => move switch
    {
        PlaylistMove.Up => "Move up",
        PlaylistMove.Down => "Move down",
        PlaylistMove.ToTop => "Move to the top",
        PlaylistMove.ToBottom => "Move to the bottom",
        _ => throw new ArgumentOutOfRangeException(nameof(move), move, "A move with no label."),
    };

    private static int IndexOf(Playlist playlist, PlaylistEntry entry)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(entry);
        for (var i = 0; i < playlist.Entries.Count; i++)
        {
            if (playlist.Entries[i] == entry)
            {
                return i;
            }
        }

        throw new ArgumentException($"The entry '{entry.Key}' is not on '{playlist.Name}'.", nameof(entry));
    }
}

/// <summary>The quiet status line under a playlist's header (mockup 04): whose it is, then how many titles.</summary>
public static class PlaylistPageSummary
{
    public static string Status(HouseholdPlaylist playlist, ViewerId viewer)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        var whose = playlist.Shared ? "Shared · both of you" : "Yours only";
        var count = playlist.List.Entries.Count;
        var titles = count switch
        {
            0 => "no titles yet",
            1 => "1 title",
            _ => $"{count} titles",
        };

        if (!playlist.IsVisibleTo(viewer))
        {
            throw new ArgumentException($"'{playlist.List.Name}' is not {viewer}'s to see (D97).", nameof(viewer));
        }

        return whose + " · " + titles;
    }
}
