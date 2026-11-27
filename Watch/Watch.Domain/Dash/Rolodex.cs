using System.Globalization;
using System.Text;

namespace Watch.Domain.Dash;

/// <summary>
/// The phone's Rolodex as the wrist holds it (P14 B6, W-5.1.2; PC6): every contact Post can text, each a name and an
/// id (never a number, an address or a photo, by the owner's ruling), in Post's own order, by name. A contact listed
/// twice is listed once. The owner finds someone by the letters they type or say into the system's input
/// (<see cref="Find"/>), or by scrolling the whole list; by voice means the name said into the search, not a matching
/// service.
/// </summary>
public sealed class Rolodex
{
    private readonly IReadOnlyList<(DashContact Contact, IReadOnlyList<string> Words)> entries;

    public Rolodex(IEnumerable<DashContact> contacts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        entries = contacts.Where(contact => seen.Add(contact.Id)).Select(contact => (contact, Words(contact.Name))).ToList();
        Contacts = entries.Select(entry => entry.Contact).ToList();
    }

    public IReadOnlyList<DashContact> Contacts { get; }

    /// <summary>
    /// The contacts the owner's letters name: every word of the query begins a word of the name, whatever the case,
    /// accents or apostrophes, and whatever punctuation a dictation adds. Names that begin with the query come first,
    /// then the rest, each in Post's order. Nothing asked (or only punctuation) is everyone.
    /// </summary>
    public IReadOnlyList<DashContact> Find(string? query)
    {
        var asked = Words(query ?? string.Empty);
        if (asked.Count == 0)
        {
            return Contacts;
        }

        var found = entries.Where(entry => asked.All(word => entry.Words.Any(said => said.StartsWith(word, StringComparison.Ordinal)))).ToList();
        return found.Where(entry => entry.Words[0].StartsWith(asked[0], StringComparison.Ordinal))
            .Concat(found.Where(entry => !entry.Words[0].StartsWith(asked[0], StringComparison.Ordinal)))
            .Select(entry => entry.Contact)
            .ToList();
    }

    // A name as words to match: lower case, accents taken off, an apostrophe joining its word ("Juniper’s" is
    // "junipers"), split at anything that is not a letter or a digit.
    private static IReadOnlyList<string> Words(string text)
    {
        var plain = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark || character is '\'' or '’' or 'ʼ')
            {
                continue;
            }
            plain.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        return plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}

/// <summary>One page asked of the phone (<paramref name="Page"/>), or what stood in the way of it.</summary>
public sealed record RolodexAnswer(DashRolodex? Page, DashTrouble? Trouble);

/// <summary>The whole Rolodex gathered, or what stood in the way of it.</summary>
public sealed record RolodexFetch(Rolodex? Whole, DashTrouble? Trouble);

/// <summary>
/// Gathering the phone's Rolodex a page at a time (P14 B6): from the first page, each in turn until the last. Every page
/// must be of the first page's edition and say the same number of pages, or the book changed on the phone between them
/// and the pages would not belong together, so the wrist starts again from the first; after <see cref="Restarts"/> it
/// says the contacts kept changing. Anything that stands in the way of a page stops the asking and is said. Bounded: at
/// most every page of the largest Rolodex, <see cref="Restarts"/> times over.
/// </summary>
public static class RolodexGathering
{
    /// <summary>How many times the wrist starts again when the book changes under it.</summary>
    public const int Restarts = 2;

    public static async Task<RolodexFetch> GatherAsync(Func<DashRolodexAsk, Task<RolodexAnswer>> ask)
    {
        var contacts = new List<DashContact>();
        string? edition = null;
        var pages = 0;
        var next = 0;
        var restarts = 0;
        while (true)
        {
            var answer = await ask(new DashRolodexAsk(next));
            if (answer.Page is not { } page)
            {
                return new RolodexFetch(null, answer.Trouble ?? DashTrouble.Unreadable);
            }

            if (page.Page != next || (next > 0 && (page.Edition != edition || page.Pages != pages)))
            {
                if (++restarts > Restarts)
                {
                    return new RolodexFetch(null, DashTrouble.Changing);
                }
                contacts.Clear();
                next = 0;
                continue;
            }

            if (next == 0)
            {
                edition = page.Edition;
                pages = page.Pages;
            }
            contacts.AddRange(page.Contacts);
            if (next + 1 >= pages)
            {
                return new RolodexFetch(new Rolodex(contacts), null);
            }
            next++;
        }
    }
}

/// <summary>
/// The Rolodex the wrist last gathered whole, held in memory for the session (Dash's process) only and never written
/// down: the phone keeps the record (A9). A screen opened again shows it at once while a fresh one is asked for. Shared
/// between a screen and the pool it asks from, so every access is locked.
/// </summary>
public sealed class HeldRolodex
{
    private readonly object gate = new();
    private Rolodex? held;

    public Rolodex? Current
    {
        get
        {
            lock (gate)
            {
                return held;
            }
        }
    }

    public void Hold(Rolodex rolodex)
    {
        lock (gate)
        {
            held = rolodex;
        }
    }
}
