using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;

namespace Pulp.Domain.ChildView;

/// <summary>A playlist as a child's view shows it: the titles on it every child watching may see.</summary>
public sealed record ChildPlaylist(HouseholdPlaylist Playlist, IReadOnlyList<TitleId> Titles);

/// <summary>
/// A child's Playlists (D42, D97): the playlists every child watching may open — their own and the
/// shared ones — each reduced to the titles every one of them may see (D280). Games wait for the TV
/// phase (D231) and a franchise cannot be judged as a title, so neither is shown. A playlist left with
/// nothing is not listed: its name would be the one place an adult's list surfaced.
/// </summary>
public static class ChildPlaylists
{
    public static IReadOnlyList<ChildPlaylist> For(
        ChildAudience audience,
        Func<ViewerId, IReadOnlyList<HouseholdPlaylist>> playlistsFor,
        Func<TitleId, TitleFacts?> factsFor)
    {
        ArgumentNullException.ThrowIfNull(audience);
        ArgumentNullException.ThrowIfNull(playlistsFor);
        ArgumentNullException.ThrowIfNull(factsFor);

        var open = audience.Watching.Select(child => playlistsFor(child).Select(list => list.Id).ToHashSet()).ToList();
        var lists = playlistsFor(audience.Watching[0]).Where(list => open.All(ids => ids.Contains(list.Id)));

        var shown = new List<ChildPlaylist>();
        foreach (var list in lists)
        {
            var titles = list.List.Entries
                .Where(entry => entry.Kind is PlaylistEntryKind.Film or PlaylistEntryKind.Show)
                .Select(entry => new TitleId(entry.Key))
                .Distinct()
                .ToList();
            var visible = audience.Visible(titles, factsFor);
            if (visible.Count > 0)
            {
                shown.Add(new ChildPlaylist(list, visible));
            }
        }

        return shown;
    }
}
