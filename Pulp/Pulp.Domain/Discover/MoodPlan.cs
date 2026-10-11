using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.Core;

namespace Pulp.Domain.Discover;

/// <summary>
/// Mood search's results (D171–D172): the real titles kept, in the model's order, and how many of
/// the model's names were dropped — made up or ambiguous, off the household's services, or hidden by
/// a child's filter. The counts are for the diagnostics log; the page shows only what was kept.
/// </summary>
public sealed record MoodResult(MoodOutcome Outcome, IReadOnlyList<CatalogueTitle> Titles, int Unresolved, int OffServices, int Hidden)
{
    public int Dropped => Unresolved + OffServices + Hidden;
}

/// <summary>
/// The pure steps of a mood search, either side of the one model call: which names to look up in
/// the catalogue, then resolving the answer against what the lookup found (Score's
/// <c>PlaylistPlan</c> rule, D104). A child's result is filtered here, after the model and the
/// catalogue have both spoken, so nothing either says can put a hidden title on a child's page.
/// </summary>
public static class MoodPlan
{
    /// <summary>Each distinct name the answer gave, once, in its order — the catalogue searches to run.</summary>
    public static IReadOnlyList<string> SearchTerms(MoodAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var terms = new List<string>();
        foreach (var candidate in answer.Candidates)
        {
            var name = candidate.Name.Trim();
            if (seen.Add(TitleNames.Normalize(name)) && name.Length > 0)
            {
                terms.Add(name);
            }
        }

        return terms;
    }

    public static MoodResult Resolve(
        MoodQuery query,
        MoodAnswer answer,
        IEnumerable<CatalogueTitle> searchResults,
        MoodViewer viewer,
        Func<TitleId, IReadOnlyCollection<FranchiseId>> franchisesOf)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(answer);
        var resolution = CandidateResolver.Resolve(
            answer.Candidates, searchResults, query.Context, viewer, franchisesOf, query.MaxTitles, []);
        return new MoodResult(answer.Outcome, resolution.Titles, resolution.Unresolved, resolution.OffServices, resolution.Hidden);
    }
}
