using System.Globalization;
using System.Text;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.Core;

namespace Pulp.Domain.Discover;

/// <summary>What resolving an answer kept, in the model's order, and how many it dropped and why.</summary>
internal sealed record Resolution(IReadOnlyList<CatalogueTitle> Titles, int Unresolved, int OffServices, int Hidden);

/// <summary>
/// The one walk both <see cref="MoodPlan"/> and <see cref="PlaylistPlan"/> take from named candidates
/// to real titles (Score's <c>PlaylistPlan</c> rule, D104). Each candidate is kept only when
/// <list type="number">
/// <item>it resolves to exactly one catalogue title — by name, then year, then kind; an ambiguous name is dropped rather than guessed;</item>
/// <item>that title streams on one of the household's services;</item>
/// <item>every child in the view is shown it by their filter (G9's order, unchanged).</item>
/// </list>
/// Repeats collapse, the seed is never handed back, and the list stops at the cap.
/// </summary>
internal static class CandidateResolver
{
    public static Resolution Resolve(
        IEnumerable<MoodCandidate> candidates,
        IEnumerable<CatalogueTitle> searchResults,
        DiscoverContext context,
        MoodViewer viewer,
        Func<TitleId, IReadOnlyCollection<FranchiseId>> franchisesOf,
        int maxTitles,
        IReadOnlyCollection<SeedTitle> excluded)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(searchResults);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(franchisesOf);
        ArgumentNullException.ThrowIfNull(excluded);

        var byName = new Dictionary<string, List<CatalogueTitle>>(StringComparer.Ordinal);
        foreach (var title in searchResults.DistinctBy(title => title.Id))
        {
            var key = TitleNames.Normalize(title.Name);
            if (key.Length == 0)
            {
                continue;
            }

            if (!byName.TryGetValue(key, out var titles))
            {
                byName[key] = titles = [];
            }

            titles.Add(title);
        }

        var services = context.Services.ToHashSet();
        var seen = new HashSet<TitleId>();
        var kept = new List<CatalogueTitle>();
        int unresolved = 0, offServices = 0, hidden = 0;

        foreach (var candidate in candidates)
        {
            if (kept.Count >= maxTitles)
            {
                break;
            }

            if (candidate is null)
            {
                continue;
            }

            var match = Match(candidate, byName);
            if (match is null)
            {
                unresolved++;
                continue;
            }

            if (!seen.Add(match.Id) || excluded.Any(seed => IsSeed(seed, match)))
            {
                continue;
            }

            if (!TitleOffers.Streams(match.Offers).Any(offer => services.Contains(offer.Service)))
            {
                offServices++;
                continue;
            }

            if (!viewer.Shows(DiscoverFacts.For(match, franchisesOf)))
            {
                hidden++;
                continue;
            }

            kept.Add(match);
        }

        return new Resolution(kept, unresolved, offServices, hidden);
    }

    private static CatalogueTitle? Match(MoodCandidate candidate, Dictionary<string, List<CatalogueTitle>> byName)
    {
        if (!byName.TryGetValue(TitleNames.Normalize(candidate.Name), out var named))
        {
            return null;
        }

        var sameKind = candidate.Kind is { } kind ? named.Where(title => title.Kind == kind).ToList() : named;
        if (candidate.Year is not { } year)
        {
            return sameKind.Count == 1 ? sameKind[0] : null;
        }

        // A show's year is when it first aired, a film's when it opened, and the model can be a year
        // out on either; the exact year still wins over a near one when both exist.
        return Only(sameKind.Where(title => title.Year == year))
            ?? Only(sameKind.Where(title => title.Year is { } known && Math.Abs(known - year) == 1))
            ?? (sameKind.Count == 1 && sameKind[0].Year is null ? sameKind[0] : null);
    }

    private static CatalogueTitle? Only(IEnumerable<CatalogueTitle> titles)
    {
        var list = titles.Take(2).ToList();
        return list.Count == 1 ? list[0] : null;
    }

    private static bool IsSeed(SeedTitle seed, CatalogueTitle title) =>
        seed.Kind == title.Kind
        && seed.Year == title.Year
        && TitleNames.Normalize(seed.Name) == TitleNames.Normalize(title.Name);
}

/// <summary>Title names compared the way a person would: no case, no accents, no punctuation, "&amp;" as "and".</summary>
internal static class TitleNames
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var decomposed = name.Replace("&", " and ", StringComparison.Ordinal).Normalize(NormalizationForm.FormD);
        var text = new StringBuilder(decomposed.Length);
        var space = true;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                text.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (!space)
            {
                text.Append(' ');
                space = true;
            }
        }

        return text.ToString().TrimEnd();
    }
}
