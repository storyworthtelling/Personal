using Constitution.Domain.Household.Records;
using Storyworthtelling.Household;

namespace Constitution.Domain.Household;

/// <summary>Whose install this is, as far as the roster is concerned.</summary>
public enum RosterRole
{
    /// <summary>The owner's install: the roster's single writer (D74).</summary>
    Owner,

    /// <summary>The other adult's install: the roster is read-only.</summary>
    Partner,
}

/// <summary>Why an edit was not made.</summary>
public enum RosterRefusalReason
{
    PartnerInstallIsReadOnly,
}

/// <summary>A refused edit: the reason as a value, and the plain sentence the screen can say.</summary>
public sealed record RosterRefusal(RosterRefusalReason Reason, string Sentence);

/// <summary>
/// What an edit came to: the document to keep (the input itself when the edit was refused, so a caller
/// cannot save a half-made change by mistake) and the refusal, if any.
/// </summary>
public sealed record RosterEditResult(HouseholdDocument Document, RosterRefusal? Refusal)
{
    public bool Accepted => Refusal is null;
}

/// <summary>
/// The roster has one writer (D74, C-6.1.4): the owner's install. The partner's install reads it and its
/// edits are refused with a reason, before any record is built. Pure — the stamp and the writer's install
/// id come from the caller, who owns the clock — and the writes go through the library's Edit and Delete
/// (<see cref="HouseholdRecords"/>), so an owner edit keeps a field a newer build wrote.
///
/// <para><b>Where the role comes from, and the limit.</b> <see cref="RoleFor"/> takes the library's
/// <see cref="HouseholdFileRole"/>, which the head derives from the household file's owner marker (the
/// vault's owner account against the signed-in account) and remembers per account
/// (<see cref="HouseholdRoleMemory"/>). Before the passphrase has restored the vault, and with nothing
/// remembered, the role is unknown. Unknown is treated as Pulp treats it — the owner's rights — because a
/// phone with no vault must be able to set its own household up, and such a phone can never create the shared
/// file. The window is accepted and closed by reading the vault at launch; it is not closed here.</para>
/// </summary>
public sealed class RosterEditor
{
    public const string PartnerReadOnlySentence = "The household roster is the owner's to change, on their phone.";

    public RosterEditor(RosterRole role) => Role = role;

    public RosterRole Role { get; }

    public bool CanEdit => Role == RosterRole.Owner;

    /// <summary>The line Settings shows where the partner would edit; null for the owner.</summary>
    public string? ReadOnlyNote => CanEdit ? null : PartnerReadOnlySentence;

    public static RosterRole RoleFor(HouseholdFileRole file) =>
        file == HouseholdFileRole.SharedWith ? RosterRole.Partner : RosterRole.Owner;

    /// <summary>Adds the person, or changes them (the id is the key, so a rename moves no record).</summary>
    public RosterEditResult Save(HouseholdDocument document, RosterPerson person, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(person);
        if (Refused() is { } refusal)
        {
            return new RosterEditResult(document, refusal);
        }

        person.EnsureValid();
        return new RosterEditResult(HouseholdRecords.Put(document, RosterMaps.Person, person, stamp, writer), null);
    }

    /// <summary>Removes the person as a tombstone, so an older live copy cannot come back on a merge.</summary>
    public RosterEditResult Remove(HouseholdDocument document, PersonId id, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (Refused() is { } refusal)
        {
            return new RosterEditResult(document, refusal);
        }

        var key = new RecordKey(ConstitutionKinds.Person, id.Value);
        return new RosterEditResult(HouseholdRecords.Remove(document, key, stamp, writer), null);
    }

    private RosterRefusal? Refused() =>
        CanEdit ? null : new RosterRefusal(RosterRefusalReason.PartnerInstallIsReadOnly, PartnerReadOnlySentence);
}
