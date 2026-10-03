using Foreword.Domain.Models;

namespace Foreword.Domain.Calendar;

/// <summary>
/// What is known about how one calendar's events reach the columns.
/// </summary>
/// <param name="People">
/// This account's people, in print order — the columns an event can be split across. Set up in
/// Foreword, never written into the code.
/// </param>
/// <param name="Column">
/// The column this calendar maps to, or null if it is <b>shared</b> — a calendar that can carry
/// several people, whose events are split by who is named on them.
/// </param>
/// <param name="Addresses">
/// Each person's email addresses. Matching is by address because it is the only signal Google gives
/// that is not a guess: a name in a title is a heuristic, an invited address is a fact.
/// </param>
/// <param name="Names">
/// Each person's name keywords, used when nobody is invited. Several per person, because families
/// do not use one spelling.
/// </param>
/// <param name="Default">
/// Where an event goes when none of the three sources can tell. It prints in one column and stays
/// wrong until the owner renames the event or adds the guest — there is no triage screen and the
/// model is never asked to guess who is going.
/// </param>
public sealed record ColumnRouting(
    IReadOnlyList<PersonColumn> People,
    PersonColumn? Column,
    IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Addresses,
    IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Names,
    PersonColumn Default);

/// <summary>
/// Who an event belongs to (decision D31). One event can belong to several people, so the answer is
/// a <b>set</b>: "swimming, Riley and Jo" is one event in two columns, not two events or a guess
/// between them.
///
/// <para>Three sources, in this order, and the order is the decision:</para>
/// <list type="number">
///   <item><b>Guests on the event.</b> An invited address is a fact where a name in a title is a
///   heuristic. <c>ResponseStatus</c> is deliberately <b>ignored</b> — a family that never answers
///   an invitation would otherwise get nothing, which is the common case rather than an edge one. A
///   guest who is none of the people is ignored entirely rather than widening the set.</item>
///   <item><b>Every name matched in the title</b>, not the first. Matching only the first is how
///   "Riley and Jo to swimming" became Riley's alone.</item>
///   <item><b>Nobody named on a shared calendar means everybody</b> — which is what a calendar
///   called Family means.</item>
/// </list>
///
/// <para>Where none of the three can tell, the event prints in one column. It is not held, not
/// flagged and not guessed at: it is simply somewhere, and stays wrong until the owner names
/// somebody on it.</para>
/// </summary>
public static class ColumnSet
{
    public static IReadOnlyList<PersonColumn> Resolve(CalendarEvent e, ColumnRouting routing)
    {
        // A calendar mapped to one person needs no splitting: its events are that person's, whoever
        // else happens to be invited. Reading guests here would move a work meeting out of the work
        // calendar's column the moment a spouse was copied in.
        if (routing.Column is { } mapped)
        {
            return [mapped];
        }

        var invited = routing.People
            .Where(person => Matches(e.Guests, routing.Addresses, person))
            .ToList();

        if (invited.Count > 0)
        {
            return invited;
        }

        var named = routing.People
            .Where(person => Named(e.Title, routing.Names, person))
            .ToList();

        if (named.Count > 0)
        {
            return named;
        }

        // Nobody named on a shared calendar: everybody. This is the branch that makes a calendar
        // called Family behave like one.
        return routing.People;
    }

    private static bool Matches(
        IReadOnlyList<string> guests,
        IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> addresses,
        PersonColumn person) =>
        addresses.TryGetValue(person, out var theirs)
        && guests.Any(guest => theirs.Any(a =>
            !string.IsNullOrWhiteSpace(a) && string.Equals(a, guest, StringComparison.OrdinalIgnoreCase)));

    private static bool Named(
        string title,
        IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> names,
        PersonColumn person) =>
        names.TryGetValue(person, out var theirs)
        && theirs.Any(n => !string.IsNullOrWhiteSpace(n)
            && title.Contains(n, StringComparison.OrdinalIgnoreCase));
}
