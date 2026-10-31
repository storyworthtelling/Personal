using Constitution.Domain.Household.Records;
using Storyworthtelling.Household;

namespace Constitution.Domain.Household;

/// <summary>Why a removal (or its undo) was not made.</summary>
public enum RemovalRefusalReason
{
    PartnerInstallIsReadOnly,
    LastAdult,
    NotOnTheRoster,
}

/// <summary>A refused removal: the reason as a value, and the plain sentence the screen can say.</summary>
public sealed record RemovalRefusal(RemovalRefusalReason Reason, string Sentence);

/// <summary>
/// What a removal took, held by the caller for the undo window and no longer: the person's record as it stood, and for a child the
/// records tied to them, each with all its fields (so a field a newer build wrote, and the private date inside the person's own
/// record, come back exactly as they were). It is a bag of fields and names no date of birth; <see cref="ToString"/> is the id only.
/// </summary>
public sealed class RemovedPerson
{
    internal RemovedPerson(PersonId id, string name, IReadOnlyList<HouseholdRecord> taken)
    {
        Id = id;
        Name = name;
        Taken = taken;
    }

    public PersonId Id { get; }

    public string Name { get; }

    internal IReadOnlyList<HouseholdRecord> Taken { get; }

    public override string ToString() => nameof(RemovedPerson) + "(" + Id.Value + ")";
}

/// <summary>A removal's result: the document to keep (the input itself when refused) and the handle for the undo, or the refusal.</summary>
public sealed record PersonRemovalResult(HouseholdDocument Document, RemovedPerson? Removed, RemovalRefusal? Refusal)
{
    public bool Accepted => Removed is not null;
}

public enum PersonRestoreOutcome
{
    Restored,

    /// <summary>Someone is on the roster under that id again (the owner added them anew); the newer record is left alone.</summary>
    AlreadyBack,
    Refused,
}

public sealed record PersonRestoreResult(HouseholdDocument Document, PersonRestoreOutcome Outcome, RemovalRefusal? Refusal);

/// <summary>
/// Taking a person off the roster, and putting them back (the person sheet's Remove and its undo). Pure: the stamp and the writer's
/// install id come from the caller, who owns the clock, and every write goes through the library's own Remove and Edit
/// (<see cref="HouseholdRecords"/>, <see cref="HouseholdRecord"/>), so a removal is a tombstone no older copy can undo on a merge, and an
/// undo is a <b>new, newer write</b> over that tombstone rather than the old stamp brought back.
///
/// <para><b>Who may.</b> Only the owner's install (<see cref="RosterEditor"/>; an unknown role has the owner's rights, decided there). The
/// last adult cannot be removed: the household needs somebody to plan for. The count is of adults this build can read, so an unreadable
/// record never makes the last readable adult look spare.</para>
///
/// <para><b>What goes with a person.</b> A child's own records go with them: their ratings, their taste notes and their meal answers
/// (every record of those kinds whose <c>person</c> field is theirs). An adult's stay, because the other adult's pages and the learned
/// taste are the household's. The planning lease is never written here: it belongs to an install, not a person, so who holds it is
/// settled only by the lease's own rules (heartbeat, silence, week boundary).</para>
/// </summary>
public static class RosterRemoval
{
    public const string NotOnTheRosterSentence = "That person is no longer on the household roster.";

    public static string LastAdultSentence(string name) =>
        $"{name} is the only adult on the household roster, so they cannot be removed. Add another adult first.";

    public static PersonRemovalResult Remove(RosterEditor editor, HouseholdDocument document, PersonId id, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(document);
        if (!editor.CanEdit)
        {
            return Refused(document, RemovalRefusalReason.PartnerInstallIsReadOnly, RosterEditor.PartnerReadOnlySentence);
        }

        var roster = RosterView.From(document);
        var entry = roster.Entries.FirstOrDefault(candidate => candidate.Id == id);
        var personKey = new RecordKey(ConstitutionKinds.Person, id.Value);
        if (entry is null || document.Live(personKey) is not { } person)
        {
            return Refused(document, RemovalRefusalReason.NotOnTheRoster, NotOnTheRosterSentence);
        }

        if (entry.Role == MemberRole.Adult && roster.Adults.Count == 1)
        {
            return Refused(document, RemovalRefusalReason.LastAdult, LastAdultSentence(entry.Name));
        }

        var taken = new List<HouseholdRecord> { person };
        if (entry.Role == MemberRole.Child)
        {
            taken.AddRange(TiedTo(document, id));
        }

        var next = taken.Aggregate(document, (current, record) => HouseholdRecords.Remove(current, record.Key, stamp, writer));
        return new PersonRemovalResult(next, new RemovedPerson(id, entry.Name, taken), null);
    }

    public static PersonRestoreResult Restore(RosterEditor editor, HouseholdDocument document, RemovedPerson removed, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(removed);
        if (!editor.CanEdit)
        {
            return new PersonRestoreResult(
                document,
                PersonRestoreOutcome.Refused,
                new RemovalRefusal(RemovalRefusalReason.PartnerInstallIsReadOnly, RosterEditor.PartnerReadOnlySentence));
        }

        if (document.Live(new RecordKey(ConstitutionKinds.Person, removed.Id.Value)) is not null)
        {
            return new PersonRestoreResult(document, PersonRestoreOutcome.AlreadyBack, null);
        }

        var next = document;
        foreach (var record in removed.Taken)
        {
            var current = next.Find(record.Key);
            if (current is { IsTombstone: false })
            {
                // Something newer already stands under this key; the undo does not write over it.
                continue;
            }

            var back = current is null
                ? HouseholdRecord.Write(record.Key, record.SchemaVersion, record.Fields, stamp, writer)
                : current.Edit(record.Fields, record.SchemaVersion, stamp, writer);
            next = next.With(back);
        }

        return new PersonRestoreResult(next, PersonRestoreOutcome.Restored, null);
    }

    private static PersonRemovalResult Refused(HouseholdDocument document, RemovalRefusalReason reason, string sentence) =>
        new(document, null, new RemovalRefusal(reason, sentence));

    // Ratings, taste notes and meal answers carry the person's id in their own "person" field.
    private static IEnumerable<HouseholdRecord> TiedTo(HouseholdDocument document, PersonId id) =>
        document.LiveRecords.Where(record =>
            IsPersonal(record.Key)
            && record.Fields.TryGetValue("person", out var owner)
            && string.Equals(owner, id.Value, StringComparison.OrdinalIgnoreCase));

    private static bool IsPersonal(RecordKey key) =>
        key.Kind == ConstitutionKinds.Rating
        || key.Kind == ConstitutionKinds.TasteNote
        || MealAnswerKeys.IsAnswer(key);
}
