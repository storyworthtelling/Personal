namespace Constitution.Domain.Calendar;

/// <summary>Finds a word or phrase in a title only where it stands alone.</summary>
internal static class WholeWord
{
    /// <summary>
    /// True when <paramref name="phrase"/> appears in <paramref name="text"/>, without regard to case,
    /// with no letter, digit, hyphen or underscore directly before or after it. "Sam" is in "Sam's
    /// turn" and "(Sam)" and is not in "Samba", "Samuel" or "Sam-Lee"; "Off" is in "Day off" and not in
    /// "Offsite", "Kickoff" or "Off-site".
    ///
    /// <para>The hyphen joins on purpose, and the strictness is the safe direction wherever this is
    /// used: a name mistaken for another word names nobody, which blocks both adults; a word mistaken
    /// for a name would name one adult and leave the other free of an event that is hers.</para>
    /// </summary>
    public static bool Contains(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        var needle = phrase.Trim();
        var from = 0;
        while (from <= text.Length - needle.Length)
        {
            var found = text.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            var before = found == 0 || !Joins(text[found - 1]);
            var afterIndex = found + needle.Length;
            var after = afterIndex >= text.Length || !Joins(text[afterIndex]);
            if (before && after)
            {
                return true;
            }

            from = found + 1;
        }

        return false;
    }

    private static bool Joins(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_';
}

/// <summary>
/// Which adults an entry names. Two sources, and an adult named by <i>either</i> is named:
/// <list type="number">
///   <item>An invited address that is theirs: a fact, matched without case.</item>
///   <item>Their name, or another name the family uses for them, standing alone in the title.</item>
/// </list>
///
/// <para>Foreword takes the guests and, only if there are none, the title (decision D31). This takes
/// both, because the guest list can name Lee while the title says "with Sam", and reading only the
/// first would leave Sam free over an event with his name on it. Taking both can only name more
/// people, which blocks more, never less.</para>
/// </summary>
internal static class NamedAdults
{
    public static List<Adult> In(CalendarEntry entry, IReadOnlyList<Adult> adults) =>
        adults.Where(adult => IsNamed(entry, adult)).ToList();

    private static bool IsNamed(CalendarEntry entry, Adult adult) =>
        entry.Guests.Any(guest => adult.Addresses.Any(address => string.Equals(address, guest, StringComparison.OrdinalIgnoreCase)))
        || WholeWord.Contains(entry.Title, adult.Name)
        || adult.OtherNames.Any(other => WholeWord.Contains(entry.Title, other));
}
