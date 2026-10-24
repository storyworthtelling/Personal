using Constitution.Domain.Household;
using Constitution.Domain.Ink;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals;

/// <summary>One row of the rating page: the circle group, who it is for, and the dish that person ate (D37).</summary>
public sealed record TasteRatingRow(string Group, PersonId Person, RecipeId Recipe);

/// <summary>One row of the meal sheet: the item's id, who answered for it, and the dish it was.</summary>
public sealed record TasteDenialRow(string SubjectId, PersonId Person, RecipeId Recipe);

/// <summary>The ratings a page gave, and the groups it did not settle (asked, not read, unreported, or a word that is no lane).</summary>
public sealed record TasteInkReading(IReadOnlyList<TasteRating> Ratings, IReadOnlyList<string> Unsettled);

/// <summary>
/// Reads the ink resolver's outcomes for the taste profile. <b>Silence never invents a rating</b>: an uncircled row is
/// <see cref="InkResolution.Unchanged"/> and adds nothing; a row the resolver asked about, could not read or never
/// reported is listed as unsettled and adds nothing. A written denial is evidence for the judge; a tick, a deadline
/// approval and a "leave it out" with no words are not.
/// </summary>
public static class TasteInk
{
    public static TasteInkReading ReadRatings(IReadOnlyList<InkResolution> resolutions, IReadOnlyList<TasteRatingRow> rows, DateOnly day, bool didntCook)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        ArgumentNullException.ThrowIfNull(rows);
        if (didntCook)
        {
            // "We didn't cook this tonight" (D68): the ratings are ignored whatever was circled.
            return new TasteInkReading([], []);
        }

        var ratings = new List<TasteRating>();
        var unsettled = new List<string>();
        foreach (var row in rows)
        {
            switch (resolutions.FirstOrDefault(resolution => resolution.SubjectId == row.Group))
            {
                case InkResolution.Unchanged:
                    break;
                case InkResolution.Circled circled when TasteWords.FromPrinted(circled.Word) is { } verdict:
                    ratings.Add(new TasteRating(row.Person, row.Recipe, day, verdict));
                    break;
                default:
                    unsettled.Add(row.Group);
                    break;
            }
        }

        return new TasteInkReading(ratings, unsettled);
    }

    /// <summary>The written denials on a meal sheet, as evidence. Words are cut to <see cref="TasteEvidence.MaxWordsLength"/>.</summary>
    public static IReadOnlyList<TasteEvidence> ReadDenials(IReadOnlyList<InkResolution> resolutions, IReadOnlyList<TasteDenialRow> rows, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        ArgumentNullException.ThrowIfNull(rows);
        var evidence = new List<TasteEvidence>();
        foreach (var row in rows)
        {
            if (resolutions.FirstOrDefault(resolution => resolution.SubjectId == row.SubjectId) is InkResolution.Denied { Reason: { } reason }
                && !string.IsNullOrWhiteSpace(reason))
            {
                var words = reason.Trim();
                evidence.Add(new TasteEvidence(
                    row.Person,
                    TasteEvidenceKind.Denial,
                    words.Length > TasteEvidence.MaxWordsLength ? words[..TasteEvidence.MaxWordsLength] : words,
                    row.Recipe,
                    day));
            }
        }

        return evidence;
    }
}
