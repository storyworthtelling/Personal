using System.Globalization;
using System.Text;

namespace Constitution.Domain.Meals.Templates;

/// <summary>The library after a merge and the templates that were added to it.</summary>
public sealed record TemplateMerged(TemplateLibrary Library, IReadOnlyList<MealTemplate> Added);

/// <summary>Takes proposals into the library as <see cref="TemplateStatus.Proposed"/>, out of the rotation until the sheet answers them.</summary>
public static class TemplateProposals
{
    /// <summary>The most proposals that may wait for an answer at once.</summary>
    public const int MaxOutstanding = 6;

    /// <summary>
    /// The library with the drafts added as proposed, in order, up to <see cref="MaxOutstanding"/> waiting at once. Words the library already holds
    /// in any status (compared without case or spacing) are not added, so a rejected line never comes back. A draft the rules refuse is left out.
    /// </summary>
    public static TemplateMerged Merge(TemplateLibrary library, IReadOnlyList<TemplateDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(drafts);
        var known = library.Templates.Select(template => MealTemplate.Key(template.Words)).ToHashSet(StringComparer.Ordinal);
        var ids = library.Templates.Select(template => template.Id).ToHashSet(StringComparer.Ordinal);
        var room = MaxOutstanding - library.Proposed.Count;
        var added = new List<MealTemplate>();
        foreach (var draft in drafts)
        {
            if (added.Count >= room)
            {
                break;
            }

            var words = MealTemplate.Normalize(draft.Words);
            if (words.Length == 0 || words.Length > MealTemplate.MaxWordsLength || !MealTemplate.Prints(words) || !known.Add(MealTemplate.Key(words)))
            {
                continue;
            }

            var id = IdFor(draft.Slot, words, ids);
            ids.Add(id);
            added.Add(new MealTemplate(id, draft.Slot, words));
        }

        return new TemplateMerged(added.Aggregate(library, (current, template) => current.With(template)), added);
    }

    private static string IdFor(MealTemplateSlot slot, string words, ISet<string> taken)
    {
        var slug = new StringBuilder();
        foreach (var letter in words.ToLowerInvariant())
        {
            if (letter is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(letter);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var stem = MealTemplate.IdPrefix + MealTemplateWords.Slots.Word(slot) + "-" + (slug.Length == 0 ? "item" : slug.ToString().Trim('-'));
        var room = TasteNote.MaxSubjectLength - 4;
        stem = (stem.Length > room ? stem[..room] : stem).TrimEnd('-');
        var id = stem;
        for (var number = 2; taken.Contains(id); number++)
        {
            id = stem + "-" + number.ToString(CultureInfo.InvariantCulture);
        }

        return id;
    }
}
