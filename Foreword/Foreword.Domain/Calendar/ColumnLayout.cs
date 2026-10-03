using Foreword.Domain.Models;

namespace Foreword.Domain.Calendar;

/// <summary>
/// The columns this account prints, left to right, and which of them belong to somebody with a
/// Google account of their own.
///
/// <para>Set up in Foreword and kept with the rest of the account's choices — never written into the
/// code. That is what lets the repository be public and a second household use the app: the code
/// knows how to route events into columns, never whose columns they are.</para>
///
/// <para><b>Who has an account matters</b> because an invitation is the one routing signal that is a
/// fact rather than a guess, and only somebody with an address can be invited. A child's days reach
/// the page on calendars a parent owns, so their column is filled by the calendar an event came from;
/// an address field offered for them could never match anything and would never say so.</para>
/// </summary>
public sealed class ColumnLayout
{
    public ColumnLayout(IReadOnlyList<PersonColumn> people, IReadOnlyList<PersonColumn> withAccounts)
    {
        People = people.Where(p => !p.IsWatch && p.Name.Length > 0).Distinct().ToList();
        WithAccounts = withAccounts.Where(People.Contains).Distinct().ToList();
    }

    /// <summary>
    /// What an install prints before its owner has set any columns up: one column, theirs. It is
    /// named for the reader rather than for anybody in particular, and renamed the first time they
    /// open the columns screen.
    /// </summary>
    public static ColumnLayout Unset { get; } = new([new PersonColumn("Me")], [new PersonColumn("Me")]);

    /// <summary>The people's columns, in print order. Never Watch.</summary>
    public IReadOnlyList<PersonColumn> People { get; }

    /// <summary>The people who can be invited to something, in print order.</summary>
    public IReadOnlyList<PersonColumn> WithAccounts { get; }

    /// <summary>Every column on the page: the people, then Watch.</summary>
    public IReadOnlyList<PersonColumn> Printed => [.. People, PersonColumn.Watch];

    public bool HasAccount(PersonColumn column) => WithAccounts.Contains(column);
}
