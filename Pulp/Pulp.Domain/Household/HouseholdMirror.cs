using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.People;
using Pulp.Domain.Settings;
using Storyworthtelling.Household;
using Storyworthtelling.People;

namespace Pulp.Domain.Household;

/// <summary>
/// <b>Foreword owns the household</b> (<see cref="HouseholdPeople"/>, one file every app reads). Pulp keeps its viewers as a mirror of it:
/// a viewer, a name and, for a child, rules (made at the strictest band when a child arrives) and an adult's Google address, written
/// through <see cref="PersonEdits"/> so the order Pulp's hand-off planner relies on holds. Each person keeps the viewer id Pulp's
/// sittings, vetoes, stars and rules already use (the household's <c>link.pulp</c>). Pure: stamps and writer come from the caller.
/// </summary>
public static class HouseholdMirror
{
    /// <summary>Pulp's viewer id for a household person: the one it linked, else the household's own id.</summary>
    public static ViewerId IdFor(HouseholdPerson person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return new ViewerId(person.Links.TryGetValue(PeopleApps.Pulp, out var linked) && !string.IsNullOrWhiteSpace(linked) ? linked : person.Id);
    }

    /// <summary>
    /// The document with its viewers made the household's. Nothing is written when nothing differs, and an empty household — one this
    /// phone hasn't met, or one Foreword hasn't set up — leaves the viewers as they are. A child's rules and leagues are kept through a
    /// rename and go when they grow up or leave.
    /// </summary>
    public static HouseholdDocument Apply(HouseholdDocument document, HouseholdPeople household, Func<HlcTimestamp> next, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(household);
        ArgumentNullException.ThrowIfNull(next);
        if (household.People.Count == 0)
        {
            return document;
        }

        var view = HouseholdView.Of(document);
        var wanted = new HashSet<ViewerId>();
        foreach (var person in household.People)
        {
            var id = IdFor(person);
            wanted.Add(id);
            var existing = view.Roster?.Viewers.FirstOrDefault(viewer => viewer.Id == id);
            var rules = view.ChildRules.FirstOrDefault(child => child.Child == id);
            var allowances = view.ChildLeagueAllowances.Where(allowance => allowance.Child == id).ToList();

            if (existing is null)
            {
                document = PersonEdits.ApplyAll(document, PersonEdits.Add(
                    new HouseholdViewer(id, person.IsChild), new ViewerNameEntry(id, person.Name),
                    person.IsChild ? ChildRuleEdits.NewChild(id) : null), next(), writer);
            }
            else
            {
                if (existing.IsChild != person.IsChild)
                {
                    document = PersonEdits.ApplyAll(document, person.IsChild
                        ? PersonEdits.MakeChild(existing, rules ?? ChildRuleEdits.NewChild(id))
                        : PersonEdits.MakeAdult(existing, rules, allowances), next(), writer);
                }

                if (!string.Equals(view.NameOf(id), person.Name, StringComparison.Ordinal))
                {
                    document = PersonEdits.ApplyAll(document, PersonEdits.Rename(id, person.Name), next(), writer);
                }
            }

            var account = person.IsChild ? null : person.GoogleAddress;
            var recorded = view.GoogleAccountOf(id);
            if (!string.Equals(account, recorded, StringComparison.OrdinalIgnoreCase))
            {
                document = account is null
                    ? HouseholdRecords.Remove(document, HouseholdMaps.AdultAccount, new AdultAccountEntry(id, recorded!), next(), writer)
                    : HouseholdRecords.Put(document, HouseholdMaps.AdultAccount, new AdultAccountEntry(id, account), next(), writer);
            }
        }

        foreach (var viewer in (view.Roster?.Viewers ?? []).Where(viewer => !wanted.Contains(viewer.Id)))
        {
            var rules = view.ChildRules.FirstOrDefault(child => child.Child == viewer.Id);
            var allowances = view.ChildLeagueAllowances.Where(allowance => allowance.Child == viewer.Id).ToList();
            document = PersonEdits.ApplyAll(document,
                PersonEdits.Remove(viewer, new ViewerNameEntry(viewer.Id, view.NameOf(viewer.Id)), rules, allowances), next(), writer);
            if (view.GoogleAccountOf(viewer.Id) is { } address)
            {
                document = HouseholdRecords.Remove(document, HouseholdMaps.AdultAccount, new AdultAccountEntry(viewer.Id, address), next(), writer);
            }
        }

        return document;
    }

    /// <summary>The viewers Pulp holds, for the owner's phone to bring into the household once.</summary>
    public static IReadOnlyList<ImportedPerson> ForImport(HouseholdView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (view.Roster?.Viewers ?? [])
            .Select(viewer => new ImportedPerson(
                viewer.Id.Value, view.NameOf(viewer.Id), viewer.IsChild, null, false, viewer.IsChild ? null : view.GoogleAccountOf(viewer.Id)))
            .ToList();
    }
}
