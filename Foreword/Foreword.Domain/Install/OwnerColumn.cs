using Foreword.Domain.Calendar;
using Foreword.Domain.Models;

namespace Foreword.Domain.Install;

/// <summary>
/// Whose phone this is.
///
/// <para>Foreword has only ever run on one, so the answer was a default rather than a setting —
/// one person's column, in four places. On a second install every one of those is wrong in the
/// same quiet way: a hand-written note with nobody named on it lands in somebody else's column,
/// nothing errors, and the event is simply on the wrong person's day. That is the whole shape of
/// "nothing is hardcoded to one person": not a rename, but the places where one person was the
/// assumption nobody had to state.</para>
///
/// <para>It is <b>inferred</b> from the signed-in account and never asked, against the columns this
/// account set up (<see cref="ColumnLayout"/>). Two facts answer it,
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
    /// <para>Only the people who have an account can be proposed. A child's column is filled by the
    /// calendar an event came from and never by an address, so proposing one from a signed-in
    /// account would mean believing a field that cannot work.</para>
    ///
    /// <para><paramref name="householdAccount"/> is the shared vault's owner. The vault has one
    /// writer and two readers: the writer is the first person with an account, and the reader that is
    /// not the writer is the second. A layout with nobody second proposes nothing for that reader. A vault nobody has claimed yet says nothing, because guessing
    /// "partner" from an empty owner would make the first phone to install the app somebody
    /// else's.</para>
    /// </summary>
    public static PersonColumn? Propose(
        string? accountEmail,
        IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> addresses,
        string? householdAccount,
        ColumnLayout layout)
    {
        if (string.IsNullOrWhiteSpace(accountEmail))
        {
            return null;
        }

        var account = accountEmail.Trim();

        foreach (var person in layout.WithAccounts)
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

        if (string.IsNullOrWhiteSpace(householdAccount) || layout.WithAccounts.Count == 0)
        {
            return null;
        }

        if (Same(householdAccount, account))
        {
            return layout.WithAccounts[0];
        }

        return layout.WithAccounts.Count > 1 ? layout.WithAccounts[1] : null;
    }

    /// <summary>
    /// The column this install belongs to: what the account proposes, else the first person with an
    /// account — what the app did before this existed — else the first column of all.
    ///
    /// <para>That last fallback matters more than it looks. A phone that has been delivering since
    /// before there was an answer must behave on the update exactly as it did the morning before —
    /// an app that quietly changes whose column a note lands in, on a version bump, is worse than
    /// one that never had the answer.</para>
    /// </summary>
    public static PersonColumn Resolve(
        string? accountEmail,
        IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> addresses,
        string? householdAccount,
        ColumnLayout layout) =>
        Propose(accountEmail, addresses, householdAccount, layout)
            ?? layout.WithAccounts.Concat(layout.People).Concat(ColumnLayout.Unset.People).First();

    // Google returns an address in whatever case it was typed.
    private static bool Same(string? a, string b) =>
        string.Equals(a?.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
