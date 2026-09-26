using Foreword.Domain.Calendar;
using Foreword.Domain.Models;

namespace Foreword.Domain.Install;

/// <summary>
/// Whose phone this is.
///
/// <para>Foreword has only ever run on one, so the answer was a default rather than a setting —
/// <c>PersonColumn.Rowan</c>, in four places. On a second install every one of those is wrong in the
/// same quiet way: a hand-written note with nobody named on it lands in somebody else's column,
/// nothing errors, and the event is simply on the wrong person's day. That is the whole shape of
/// "nothing is hardcoded to one person": not a rename, but the places where one person was the
/// assumption nobody had to state.</para>
///
/// <para>It is <b>inferred</b> from the signed-in account and never asked. Two facts answer it,
/// both of which the app already has: an address entered on the calendar screen names the person
/// outright, and otherwise the household's shared vault records the account that owns it — the
/// account holder — so the phone signed in to that account is theirs and a phone signed in to any
/// other account is their partner's.</para>
/// </summary>
public static class OwnerColumn
{
    /// <summary>
    /// Who the signed-in account looks like, or nothing.
    ///
    /// <para>Only the people who have a mailbox can be proposed. A child's column is filled by the
    /// calendar an event came from and never by an address, so proposing one from a signed-in
    /// account would mean believing a field that cannot work.</para>
    ///
    /// <para><paramref name="householdAccount"/> is the shared vault's owner. The vault has one
    /// writer and two readers: the writer is the first person with a mailbox, and the reader that is
    /// not the writer is the second. A vault nobody has claimed yet says nothing, because guessing
    /// "partner" from an empty owner would make the first phone to install the app somebody
    /// else's.</para>
    /// </summary>
    public static PersonColumn? Propose(
        string? accountEmail,
        IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> addresses,
        string? householdAccount)
    {
        if (string.IsNullOrWhiteSpace(accountEmail))
        {
            return null;
        }

        var account = accountEmail.Trim();

        foreach (var person in PersonIdentity.WithMailboxes)
        {
            if (!addresses.TryGetValue(person, out var theirs))
            {
                continue;
            }

            if (theirs.Any(address => Same(address, account)))
            {
                return person;
            }
        }

        if (string.IsNullOrWhiteSpace(householdAccount))
        {
            return null;
        }

        return Same(householdAccount, account)
            ? PersonIdentity.WithMailboxes[0]
            : PersonIdentity.WithMailboxes[1];
    }

    /// <summary>
    /// The column this install belongs to: what the account proposes, else what the app did before
    /// this existed.
    ///
    /// <para>That last fallback matters more than it looks. A phone that has been delivering since
    /// before there was an answer must behave on the update exactly as it did the morning before —
    /// an app that quietly changes whose column a note lands in, on a version bump, is worse than
    /// one that never had the answer.</para>
    /// </summary>
    public static PersonColumn Resolve(
        string? accountEmail,
        IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> addresses,
        string? householdAccount) =>
        Propose(accountEmail, addresses, householdAccount)
            ?? PersonIdentity.WithMailboxes[0];

    // Google returns an address in whatever case it was typed.
    private static bool Same(string? a, string b) =>
        string.Equals(a?.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
