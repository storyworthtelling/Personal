using Pulp.Domain.Core;

namespace Pulp.Domain.People;

/// <summary>Whether the phone knows whose private lists to show.</summary>
public enum ThisPhoneStatus
{
    /// <summary>Nobody is on the roster yet, so there is nothing of anyone's to show.</summary>
    NoRoster,

    /// <summary>A roster exists but does not settle which adult holds this phone.</summary>
    Unmatched,

    Known,
}

public sealed record ThisPhoneAnswer(ThisPhoneStatus Status, ViewerId? Viewer);

/// <summary>
/// Whose phone this is — the one answer every screen asks before it shows anything private (D97:
/// each viewer's Continue watching, Recently watched, personal playlists and Friday stack are theirs
/// alone). Release C's screens each answered it privately and the copies drifted; one took the
/// roster's first adult, which in a two-adult household shows one adult the other's private list.
///
/// <para>The rule, once: the install's <b>recorded</b> viewer (kept in the install's own file, never
/// in the shared household file — a second phone restoring it would become the first phone's
/// owner), provided that viewer is still an <b>adult on the roster</b>. With no record, the roster's
/// <b>only</b> adult, the one answer that cannot be wrong. Otherwise nobody — a record that no longer
/// holds (the viewer left the roster, or is now a child) is not quietly swapped for whoever is left,
/// and two adults are never guessed between. A child is never the phone's viewer here; a child's
/// page is Favorites (D213).</para>
/// </summary>
public static class ThisPhone
{
    /// <summary>What every page says when the answer is nobody but a roster exists. Never voiced.</summary>
    public const string UnmatchedSentence = "This phone isn't matched to anyone in the household yet.";

    public static ThisPhoneAnswer Resolve(HouseholdRoster? roster, ViewerId? recorded)
    {
        if (roster is null)
        {
            return new ThisPhoneAnswer(ThisPhoneStatus.NoRoster, null);
        }

        if (recorded is { } viewer)
        {
            return CanBeRecorded(roster, viewer)
                ? new ThisPhoneAnswer(ThisPhoneStatus.Known, viewer)
                : new ThisPhoneAnswer(ThisPhoneStatus.Unmatched, null);
        }

        var adults = roster.Viewers.Where(entry => !entry.IsChild).ToList();
        return adults.Count == 1
            ? new ThisPhoneAnswer(ThisPhoneStatus.Known, adults[0].Id)
            : new ThisPhoneAnswer(ThisPhoneStatus.Unmatched, null);
    }

    /// <summary>Whether <paramref name="viewer"/> may be recorded as this phone's: an adult on the roster.</summary>
    public static bool CanBeRecorded(HouseholdRoster? roster, ViewerId viewer) =>
        roster is not null && roster.Contains(viewer) && !roster.IsChild(viewer);
}
