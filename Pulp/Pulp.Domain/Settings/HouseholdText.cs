using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.People;

namespace Pulp.Domain.Settings;

/// <summary>The Household page's words (D88, D97, D272): whose phone this is, and each person's line.</summary>
public static class HouseholdText
{
    public const string NoRoster = "Add the people in the household first.";

    /// <summary>"This phone is Sam's."; the shared sentence when no adult is matched; a nudge when there is no roster.</summary>
    public static string ThisPhone(ThisPhoneAnswer answer, Func<ViewerId, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(nameOf);
        return answer switch
        {
            { Status: ThisPhoneStatus.Known, Viewer: { } viewer } => $"This phone is {nameOf(viewer)}'s.",
            { Status: ThisPhoneStatus.NoRoster } => NoRoster,
            _ => People.ThisPhone.UnmatchedSentence,
        };
    }

    /// <summary>"Adult", or "Child · 7 to 9" with the child's band.</summary>
    public static string Person(bool isChild, AgeBand? ceiling) =>
        !isChild ? "Adult"
        : ceiling is { } band ? "Child · " + ChildRuleEdits.BandLabel(band)
        : "Child";

    public static string RemoveUndo(string name) => Undo("remove", name);

    /// <summary>The dog-ear's back-turn line, as the action card words its own (§13): "Undo unblock, Lantern."</summary>
    public static string Undo(string act, string what)
    {
        if (string.IsNullOrWhiteSpace(act) || string.IsNullOrWhiteSpace(what))
        {
            throw new ArgumentException("An undo names what it undoes and what it was done to.", string.IsNullOrWhiteSpace(act) ? nameof(act) : nameof(what));
        }

        return $"Undo {act.Trim()}, {what.Trim()}.";
    }
}
