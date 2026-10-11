using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Library;

namespace Pulp.Domain.Discover;

/// <summary>A model-made playlist once resolved: its name, the real titles kept, and what was dropped and why.</summary>
public sealed record MadePlaylist(string Name, IReadOnlyList<CatalogueTitle> Titles, int Unresolved, int OffServices, int Hidden)
{
    /// <summary>An ordinary, hand-orderable playlist (D103) holding the kept titles in the model's order.</summary>
    public Playlist ToPlaylist() =>
        Titles.Aggregate(Playlist.Named(Name), (playlist, title) => playlist.Add(PlaylistEntry.ForTitle(title.Id, title.Kind)));
}

/// <summary>
/// The model-made playlist's last pure step (D104): the answer resolved against the catalogue with
/// the same rule as mood search — real, on our services, shown to every child in the view — and the
/// seed itself never handed back. The name is the model's, or the seed's when it offered none.
/// </summary>
public static class PlaylistPlan
{
    /// <summary>A playlist name is a line on a row, not a sentence.</summary>
    public const int MaxNameLength = 60;

    public static MadePlaylist Resolve(
        PlaylistQuery query,
        MoodAnswer answer,
        IEnumerable<CatalogueTitle> searchResults,
        MoodViewer viewer,
        Func<TitleId, IReadOnlyCollection<FranchiseId>> franchisesOf)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(answer);

        var resolution = CandidateResolver.Resolve(
            answer.Candidates, searchResults, query.Context, viewer, franchisesOf, query.MaxTitles, query.Seed.Titles);
        var name = string.IsNullOrWhiteSpace(answer.Title) ? query.Seed.Name : answer.Title.Trim();
        if (name.Length > MaxNameLength)
        {
            name = name[..MaxNameLength].TrimEnd();
        }

        return new MadePlaylist(name, resolution.Titles, resolution.Unresolved, resolution.OffServices, resolution.Hidden);
    }
}
