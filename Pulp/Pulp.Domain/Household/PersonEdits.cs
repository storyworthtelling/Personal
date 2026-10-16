using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.People;

namespace Pulp.Domain.Household;

public enum PersonRecord
{
    Viewer,
    Name,
    Rules,

    /// <summary>One league a parent ticked for a child (D317): it goes and comes back with the child.</summary>
    LeagueAllowance,
}

public enum PersonWriteKind
{
    Put,
    Remove,
}

/// <summary>One write of one of a person's three records; only the value its record names is set.</summary>
public sealed record PersonStep(
    PersonWriteKind Kind,
    PersonRecord Record,
    HouseholdViewer? Viewer,
    ViewerNameEntry? Name,
    ChildRules? Rules,
    ChildLeagueAllowance? Allowance = null);

/// <summary>The writes that make one change to a person, in the order they are to land.</summary>
public sealed record PersonEdit(IReadOnlyList<PersonStep> Steps);

/// <summary>
/// A person is three records — the viewer, their name and, for a child, their rules — and the hand-off
/// planner on the receiving phone refuses a household where a child has no rules or where rules belong
/// to someone who is not a child. This is the one place the order of those writes is decided.
///
/// <para>Order: a removal takes the rules, then the name, then the viewer, so the rules never outlive
/// the child they describe; an addition puts the viewer, then the name, then the rules; changing a
/// flag to child puts the viewer then the rules, and to adult drops the rules then writes the viewer.
/// A change that involves no child (an adult added or removed, a rename) passes through no refused
/// state at all. A change that does involve one cannot: with the viewer and the rules as two separate
/// writes, either order leaves one refused state between them (the test enumerates both). So
/// <see cref="ApplyAll"/> applies every step to the document in one edit, which is what a head with an
/// atomic household edit should use; the stepwise order is the fallback that keeps the window to one
/// short step and never leaves rules standing for a person who has been removed.</para>
/// </summary>
public static class PersonEdits
{
    /// <summary>A child's <paramref name="allowances"/> are put back after the rules (an undo of a removal); an adult has none.</summary>
    public static PersonEdit Add(HouseholdViewer viewer, ViewerNameEntry name, ChildRules? rules, IReadOnlyList<ChildLeagueAllowance>? allowances = null)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(name);
        var steps = new List<PersonStep>
        {
            new(PersonWriteKind.Put, PersonRecord.Viewer, viewer, null, null),
            new(PersonWriteKind.Put, PersonRecord.Name, null, name, null),
        };
        if (viewer.IsChild)
        {
            steps.Add(new(PersonWriteKind.Put, PersonRecord.Rules, null, null, rules ?? throw new ArgumentException("A child is added with rules.", nameof(rules))));
            steps.AddRange(AllowanceSteps(PersonWriteKind.Put, allowances));
        }

        return new PersonEdit(steps);
    }

    /// <summary>The name record is dropped only when <paramref name="name"/> is given (a person never named has none).</summary>
    /// <summary>
    /// The child's league allowances go with them: a later child given the same id must not inherit every league
    /// ticked for this one (child safety), so each is tombstoned too and the undo puts them back in the same edit.
    /// </summary>
    public static PersonEdit Remove(HouseholdViewer viewer, ViewerNameEntry? name, ChildRules? rules, IReadOnlyList<ChildLeagueAllowance>? allowances = null)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var steps = new List<PersonStep>(AllowanceSteps(PersonWriteKind.Remove, allowances));
        if (rules is not null)
        {
            steps.Add(new(PersonWriteKind.Remove, PersonRecord.Rules, null, null, rules));
        }

        if (name is not null)
        {
            steps.Add(new(PersonWriteKind.Remove, PersonRecord.Name, null, name, null));
        }

        steps.Add(new(PersonWriteKind.Remove, PersonRecord.Viewer, viewer, null, null));
        return new PersonEdit(steps);
    }

    public static PersonEdit MakeChild(HouseholdViewer viewer, ChildRules rules, IReadOnlyList<ChildLeagueAllowance>? allowances = null)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(rules);
        var steps = new List<PersonStep>
        {
            new(PersonWriteKind.Put, PersonRecord.Viewer, viewer with { IsChild = true }, null, null),
            new(PersonWriteKind.Put, PersonRecord.Rules, null, null, rules),
        };
        steps.AddRange(AllowanceSteps(PersonWriteKind.Put, allowances));
        return new PersonEdit(steps);
    }

    /// <summary>A person made an adult keeps no rules; <paramref name="rules"/> is null when none were ever written.</summary>
    public static PersonEdit MakeAdult(HouseholdViewer viewer, ChildRules? rules, IReadOnlyList<ChildLeagueAllowance>? allowances = null)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var steps = new List<PersonStep>(AllowanceSteps(PersonWriteKind.Remove, allowances));
        if (rules is not null)
        {
            steps.Add(new(PersonWriteKind.Remove, PersonRecord.Rules, null, null, rules));
        }

        steps.Add(new(PersonWriteKind.Put, PersonRecord.Viewer, viewer with { IsChild = false }, null, null));
        return new PersonEdit(steps);
    }

    public static PersonEdit Rename(ViewerId viewer, string name) =>
        new([new(PersonWriteKind.Put, PersonRecord.Name, null, new ViewerNameEntry(viewer, name), null)]);

    /// <summary>One step applied to the document.</summary>
    public static HouseholdDocument Apply(HouseholdDocument document, PersonStep step, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(step);
        var put = step.Kind == PersonWriteKind.Put;
        return step.Record switch
        {
            PersonRecord.Viewer => Write(document, HouseholdMaps.Viewer, step.Viewer, put, stamp, writer),
            PersonRecord.Name => Write(document, HouseholdMaps.ViewerName, step.Name, put, stamp, writer),
            PersonRecord.Rules => Write(document, HouseholdMaps.ChildRules, step.Rules, put, stamp, writer),
            PersonRecord.LeagueAllowance => Write(document, HouseholdMaps.ChildLeague, step.Allowance, put, stamp, writer),
            _ => throw new ArgumentOutOfRangeException(nameof(step), step.Record, "Unknown person record."),
        };
    }

    /// <summary>Every step of the edit in one document, so no reader of the result sees a half-made change.</summary>
    public static HouseholdDocument ApplyAll(HouseholdDocument document, PersonEdit edit, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return edit.Steps.Aggregate(document, (current, step) => Apply(current, step, stamp, writer));
    }

    private static IEnumerable<PersonStep> AllowanceSteps(PersonWriteKind kind, IReadOnlyList<ChildLeagueAllowance>? allowances) =>
        (allowances ?? []).Select(allowance => new PersonStep(kind, PersonRecord.LeagueAllowance, null, null, null, allowance));

    private static HouseholdDocument Write<T>(HouseholdDocument document, RecordMap<T> map, T? value, bool put, HlcTimestamp stamp, string writer)
        where T : class =>
        value is null
            ? throw new InvalidOperationException($"A {map.Kind} step carries no value.")
            : put
                ? HouseholdRecords.Put(document, map, value, stamp, writer)
                : HouseholdRecords.Remove(document, map, value, stamp, writer);
}
