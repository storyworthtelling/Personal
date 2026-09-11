using Foreword.Domain.Models;

namespace Foreword.Domain.Calendar;

/// <summary>
/// Which of the four has an email address of their own.
///
/// <para>The parents do; the girls do not. Their days reach the page on calendars shared between
/// the parents, so a child's column is filled by the calendar an event came from and never by an
/// invitation — nobody invites them to anything, because there is no account to invite.</para>
///
/// <para>It is here rather than assumed on a screen because an address field offered for a child is
/// a field that cannot work: whatever is typed into it never matches a guest, nothing errors, and
/// the events go on landing exactly where they were landing before. That is the failure that looks
/// like a settings screen doing nothing at all.</para>
/// </summary>
public static class PersonIdentity
{
    /// <summary>In column order, so a screen listing them matches the page.</summary>
    public static IReadOnlyList<PersonColumn> WithMailboxes { get; } =
        [PersonColumn.Matt, PersonColumn.Ashley];

    public static bool HasMailbox(PersonColumn person) => WithMailboxes.Contains(person);
}
