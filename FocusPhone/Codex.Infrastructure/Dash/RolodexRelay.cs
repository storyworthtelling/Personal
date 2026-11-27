using System.Globalization;
using Watch.Domain.Dash;

namespace Codex.Infrastructure.Dash;

/// <summary>
/// One person in the phone's address book as Post's Rolodex reads them (<c>ContactPicker</c>): the device contact's id,
/// its display name, and its numbers in the Rolodex's own order (<see cref="Codex.Domain.Post.RolodexNumbers"/>: primary,
/// then a mobile, then the rest). Addresses are not read: a text goes to a number.
/// </summary>
public sealed record RolodexPerson(long ContactId, string Name, IReadOnlyList<string> Phones);

/// <summary>
/// Post's half of the Rolodex on the wrist (P14 B6, W-5.1.2; PC6: one address book, Post's). The owner's ruling: Post
/// sends the whole Rolodex, each contact its name and a digest id of its device contact id (<see cref="DashIds.Contact"/>),
/// never a number, an address or a photo. So a contact the wrist could not text (no number) is left out, and so is one
/// whose name is no name of its own: the phone names a nameless contact after its number or address, which must not
/// cross. Names are one line, cut to the wire's limit, with nothing in them the codec would have to escape at length.
///
/// <para>The book goes in Post's own order (by name), at most <see cref="DashLimits.RolodexPages"/> pages of
/// <see cref="DashLimits.RolodexPage"/>; each page names the book's edition, a digest of every id and name shown, so the
/// wrist can tell pages that belong together. The way back is <see cref="NumberOf"/>: a new text names the contact's id,
/// and Post finds its number here, on the phone, at the moment it sends. Pure: the head reads the address book.</para>
/// </summary>
public sealed class RolodexRelay
{
    // A run of seven digits or more, with the separators people put between them, is a number however it is written.
    private const int NumberRun = 7;

    private readonly IReadOnlyList<(DashContact Contact, string Number)> entries;
    private readonly string edition;

    public RolodexRelay(IReadOnlyList<RolodexPerson> people)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        entries = people
            .Select(person => (Person: person, Name: Clean(person.Name), Number: person.Phones.FirstOrDefault(DashRelay.Dialable)))
            .Where(entry => entry.Number is not null && IsAName(entry.Name))
            .Select(entry => (Contact: new DashContact(DashIds.Contact(entry.Person.ContactId.ToString(CultureInfo.InvariantCulture)), entry.Name), Number: entry.Number!))
            .Where(entry => seen.Add(entry.Contact.Id))
            .Take(DashLimits.RolodexPages * DashLimits.RolodexPage)
            .ToList();
        edition = DashIds.Edition(string.Join("\n", entries.Select(entry => entry.Contact.Id + "\t" + entry.Contact.Name)));
        Pages = Math.Max(1, (entries.Count + DashLimits.RolodexPage - 1) / DashLimits.RolodexPage);
    }

    /// <summary>How many pages the book takes: at least one, which is empty for an empty book.</summary>
    public int Pages { get; }

    /// <summary>One page of the book, from the first (0); null past the last.</summary>
    public DashRolodex? Page(int page) =>
        page < 0 || page >= Pages
            ? null
            : new DashRolodex(page, Pages, edition, entries.Skip(page * DashLimits.RolodexPage).Take(DashLimits.RolodexPage).Select(entry => entry.Contact).ToList());

    /// <summary>The number a new text to the contact goes to: its first number that is a number; null for anyone else.</summary>
    public string? NumberOf(string contact)
    {
        foreach (var entry in entries)
        {
            if (entry.Contact.Id == contact)
            {
                return entry.Number;
            }
        }
        return null;
    }

    // One line, at most the wire's longest, without control characters or half a surrogate pair (which the codec writes
    // as six bytes each, and which no name needs).
    private static string Clean(string name)
    {
        var kept = new System.Text.StringBuilder(name.Length);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsHighSurrogate(character) && index + 1 < name.Length && char.IsLowSurrogate(name[index + 1]))
            {
                kept.Append(character).Append(name[++index]);
            }
            else if (char.IsWhiteSpace(character))
            {
                kept.Append(' ');
            }
            else if (!char.IsControl(character) && !char.IsSurrogate(character))
            {
                kept.Append(character);
            }
        }
        return DashRelay.Cut(DashRelay.OneLine(kept.ToString()), DashLimits.Name);
    }

    // A name of its own: some letter in it, no address, and no number however it is written.
    private static bool IsAName(string name)
    {
        if (!name.Any(char.IsLetter) || name.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        var digits = 0;
        foreach (var character in name)
        {
            if (char.IsAsciiDigit(character))
            {
                if (++digits >= NumberRun)
                {
                    return false;
                }
            }
            else if (character is not (' ' or '-' or '.' or '(' or ')' or '+'))
            {
                digits = 0;
            }
        }
        return true;
    }
}
