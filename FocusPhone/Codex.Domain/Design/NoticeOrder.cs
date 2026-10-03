namespace Codex.Domain.Design;

/// <summary>
/// The order of the notices waiting behind a dog-ear (DESIGN.md §13, mockup 17): newest first BY
/// TIMESTAMP, and stable.
///
/// <para>The fold used to insert every notice at index 0 each time its owner stated it, so a notice's
/// place meant "the one touched last". A screen that re-states its notices on every render therefore
/// reshuffled them, and the page showed the reverse of the order they were built in. Here a notice
/// keeps its place until what it SAYS changes.</para>
///
/// <para><b>The rule.</b> Notices that carry a date come first, the later date first, since only they
/// can be put on a clock. Among notices on the same clock — equal dates, or none at all — the one first
/// set most recently goes first, which is the order the corner always had, so a screen with no
/// timestamps behaves exactly as it did. Setting a key again with the same date and the same
/// <i>fingerprint</i> changes nothing; a changed fingerprint (new words, role or action label) is a
/// new notice and goes to the front of its equals.</para>
///
/// <para>The fingerprint is the caller's, because only the caller knows what counts as "the same
/// notice": an action delegate is a fresh lambda on every render and must not make an unchanged notice
/// look new. This type keeps no notices, only keys, dates and where they stand, so the native fold
/// (whose notice carries a colour) and the MAUI fold (whose carries a role) share it as they share
/// <c>BinderTabLayout</c> — a byte-parallel copy kept honest by a parity test.</para>
/// </summary>
public sealed class NoticeOrder
{
    private sealed record Entry(string Key, DateTimeOffset? At, string Fingerprint, long Sequence);

    private readonly List<Entry> entries = [];
    private long next;

    /// <summary>How many notices are waiting.</summary>
    public int Count => entries.Count;

    /// <summary>The keys in display order: dated newest first, then the rest, most recently set first.</summary>
    public IReadOnlyList<string> Keys =>
        entries
            .OrderByDescending(entry => entry.At.HasValue)
            .ThenByDescending(entry => entry.At ?? DateTimeOffset.MinValue)
            .ThenByDescending(entry => entry.Sequence)
            .Select(entry => entry.Key)
            .ToList();

    /// <summary>
    /// States one owner's notice. Returns whether anything a reader could see changed — a new notice,
    /// or one whose date or fingerprint is different. Saying the same thing again returns false and
    /// moves nothing.
    /// </summary>
    public bool Set(string key, DateTimeOffset? at, string fingerprint)
    {
        var index = entries.FindIndex(entry => entry.Key == key);
        if (index >= 0 && entries[index].At == at && entries[index].Fingerprint == fingerprint)
        {
            return false;
        }

        if (index >= 0)
        {
            entries.RemoveAt(index);
        }

        entries.Add(new Entry(key, at, fingerprint, next++));
        return true;
    }

    /// <summary>Withdraws one owner's notice. Returns whether there was one to withdraw.</summary>
    public bool Remove(string key) => entries.RemoveAll(entry => entry.Key == key) > 0;

    /// <summary>Forgets everything. Returns whether anything was waiting.</summary>
    public bool Clear()
    {
        var any = entries.Count > 0;
        entries.Clear();
        return any;
    }
}
