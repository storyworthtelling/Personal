using Foreword.Domain.Calendar;
using Foreword.Domain.Models;

namespace Foreword.Domain.Install;

/// <summary>
/// Whose phone this is.
///
/// <para>Foreword has only ever run on one, so the answer was a default rather than a setting —
/// <c>PersonColumn.Matt</c>, in four places. On a second install every one of those is wrong in the
/// same quiet way: a hand-written note with nobody named on it lands in somebody else's column,
/// nothing errors, and the event is simply on the wrong person's day. That is the whole shape of
/// "nothing is hardcoded to one person": not a rename, but the places where one person was the
/// assumption nobody had to state.</para>
///
/// <para>It is <b>proposed</b> from the signed-in account rather than asked for, because the app
/// already has that fact and the calendar screen already knows which addresses belong to whom. An
/// explicit choice always wins over the proposal — somebody signed in to a shared household account
/// has to be able to say who they are and be believed.</para>
/// </summary>
public static class OwnerColumn
{
    /// <summary>
    /// Who the signed-in account looks like, or nothing.
    ///
    /// <para>Only the people who have a mailbox can be proposed. A child's column is filled by the
    /// calendar an event came from and never by an address, so proposing one from a signed-in
    /// account would mean believing a field that cannot work.</para>
    /// </summary>
    public static PersonColumn? Propose(
        string? accountEmail,
        IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> addresses)
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

            if (theirs.Any(address => string.Equals(
                address?.Trim(), account, StringComparison.OrdinalIgnoreCase)))
            {
                return person;
            }
        }

        return null;
    }

    /// <summary>
    /// The column this install belongs to: what the owner chose, else what the account proposes,
    /// else what the app did before this setting existed.
    ///
    /// <para>That last fallback matters more than it looks. A phone that has been delivering since
    /// before there was a setting must behave on the update exactly as it did the morning before —
    /// an app that quietly changes whose column a note lands in, on a version bump, is worse than
    /// one that never had the setting.</para>
    /// </summary>
    public static PersonColumn Resolve(
        PersonColumn? chosen,
        string? accountEmail,
        IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> addresses) =>
        chosen
            ?? Propose(accountEmail, addresses)
            ?? PersonIdentity.WithMailboxes[0];
}
