namespace Codex.Domain.Plate;

/// <summary>One person from the address book, as the chooser shows them.</summary>
public sealed record PickablePerson(string ContactId, string Name);

/// <summary>
/// Narrows the address book to the people the owner might mean when a suggested name was wrong
/// (the editor's "someone else", which corrects the face through <see cref="FaceBook.Correct"/>).
/// A word that starts a name ranks above a match inside one; the person wrongly suggested is left
/// out, since choosing them again would undo the correction being made.
/// </summary>
public static class FacePicker
{
    /// <summary>How many rows the card shows; typing narrows past it.</summary>
    public const int MaxOffered = 8;

    public static IReadOnlyList<PickablePerson> Choose(
        IReadOnlyList<PickablePerson> people, string typed, string? wronglySuggested)
    {
        var query = typed.Trim();
        var candidates = people.Where(person => person.ContactId != wronglySuggested);
        if (query.Length == 0)
        {
            return candidates.Take(MaxOffered).ToList();
        }

        return candidates
            .Select(person => (person, rank: Rank(person.Name, query)))
            .Where(entry => entry.rank >= 0)
            .OrderBy(entry => entry.rank)
            .Select(entry => entry.person)
            .Take(MaxOffered)
            .ToList();
    }

    // 0: a word of the name starts with what was typed; 1: it appears inside the name; -1: no match.
    private static int Rank(string name, string query)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(word => word.StartsWith(query, StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }
        return name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 : -1;
    }
}
