using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Storyworthtelling.Household;

namespace Constitution.Domain.Settings;

/// <summary>What the person sheet's pinned bar can offer. The model lists the ones that apply; the view paints exactly those.</summary>
public enum PersonSheetAction
{
    Back,
    Remove,
    Save,
}

/// <summary>
/// The person sheet (book p.17, opened from a person in the Household list): who they are, as the roster already holds it, and what
/// may be done. <b>It has no birthdate and no age</b>: the roster keeps a date of birth in the person's own record and offers no age
/// band, so the sheet has nothing to show for either and no member that could hold one (it is built from <see cref="RosterEntry"/>,
/// which has none). Only the owner's install edits (<see cref="RosterEditor"/>, whose unknown-role rule is reused, not decided again);
/// the partner's sheet offers <see cref="PersonSheetAction.Back"/> alone and says why. The last adult has no Remove and the sheet says why.
/// </summary>
public sealed record PersonSheetModel(
    PersonId Id,
    string Title,
    string RoleLine,
    string? PickupLine,
    string? CalendarLine,
    bool CanEdit,
    string? ReadOnlyNote,
    string? KeepNote,
    IReadOnlyList<PersonSheetAction> Actions)
{
    public static string LabelOf(PersonSheetAction action) => action switch
    {
        PersonSheetAction.Back => "Back",
        PersonSheetAction.Remove => "Remove",
        PersonSheetAction.Save => "Save",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "That action has no label yet."),
    };

    /// <summary>The sheet for one person, or null when they are not on the roster.</summary>
    public static PersonSheetModel? Build(RosterView roster, PersonId id, RosterRole role)
    {
        ArgumentNullException.ThrowIfNull(roster);
        var entry = roster.Entries.FirstOrDefault(candidate => candidate.Id == id);
        if (entry is null)
        {
            return null;
        }

        var editor = new RosterEditor(role);
        var isLastAdult = entry.Role == MemberRole.Adult && roster.Adults.Count == 1;
        var actions = new List<PersonSheetAction> { PersonSheetAction.Back };
        if (editor.CanEdit)
        {
            if (!isLastAdult)
            {
                actions.Add(PersonSheetAction.Remove);
            }

            actions.Add(PersonSheetAction.Save);
        }

        return new PersonSheetModel(
            entry.Id,
            entry.Name,
            entry.Role == MemberRole.Child ? "Child" : "Adult",
            entry.Role == MemberRole.Adult ? (entry.OnPickup ? "On pickup" : "Not on pickup") : null,
            entry.GoogleAddress is { } address ? "Calendar shared with " + address : null,
            editor.CanEdit,
            editor.ReadOnlyNote,
            editor.CanEdit && isLastAdult ? RosterRemoval.LastAdultSentence(entry.Name) : null,
            actions);
    }

    /// <summary>The plain sentence for a thing wrong with the sheet.</summary>
    public static string Describe(PersonProblem problem) => problem switch
    {
        PersonProblem.NameMissing => "Give them a name.",
        PersonProblem.NameTooLong => $"A name can be at most {RosterPerson.MaxNameLength} characters.",
        PersonProblem.ChildOnPickup => "A child is never on pickup; pickup is an adult's.",
        PersonProblem.AddressShape => "That is not an address: it needs one @ with something on each side and no spaces.",
        PersonProblem.NotOnTheRoster => RosterRemoval.NotOnTheRosterSentence,
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "That problem has no words yet."),
    };
}

/// <summary>An edit from the sheet: the document to keep (the input itself when nothing was saved), what was wrong, or the refusal.</summary>
public sealed record PersonSheetSave(HouseholdDocument Document, IReadOnlyList<PersonProblem> Problems, RosterRefusal? Refusal)
{
    public bool Saved => Problems.Count == 0 && Refusal is null;
}

/// <summary>
/// Saves what the sheet edits (name, pickup, Google address) over the person's record, checked by the roster's own rules
/// (<see cref="HouseholdPersonSheet.Check"/>). The role is not edited here. The person's existing birthdate is read from the record
/// and handed back to the same write, so an edit never clears it, and the sheet's caller never holds one.
/// </summary>
public static class PersonSheetEdit
{
    public static PersonSheetSave Apply(
        RosterEditor editor, HouseholdDocument document, PersonId id, string name, bool onPickup, string? googleAddress, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(document);
        if (!editor.CanEdit)
        {
            return new PersonSheetSave(document, [], new RosterRefusal(RosterRefusalReason.PartnerInstallIsReadOnly, RosterEditor.PartnerReadOnlySentence));
        }

        var existing = HouseholdRecords.Read(document, RosterMaps.Person).Items.FirstOrDefault(person => person.Id == id);
        if (existing is null)
        {
            return new PersonSheetSave(document, [PersonProblem.NotOnTheRoster], null);
        }

        var draft = new HouseholdPersonDraft(id, name, existing.Role, onPickup, googleAddress);
        var check = HouseholdPersonSheet.Check(editor, RosterView.From(document), draft, existing.Birthdate);
        if (check.Refusal is not null || check.Person is null)
        {
            return new PersonSheetSave(document, check.Problems, check.Refusal);
        }

        var saved = editor.Save(document, check.Person, stamp, writer);
        return new PersonSheetSave(saved.Document, [], saved.Refusal);
    }
}
