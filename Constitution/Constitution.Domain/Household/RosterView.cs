using Constitution.Domain.Household.Records;
using Storyworthtelling.Household;

namespace Constitution.Domain.Household;

/// <summary>
/// One row of the roster as a list, a label or a screen shows it. <b>It has no birthdate</b> — not hidden,
/// absent: there is no member that could hold one, so no row can print it by accident. That is the shape
/// <c>BirthdateShapeTests</c> holds for every view.
/// </summary>
public sealed record RosterEntry(PersonId Id, string Name, MemberRole Role, bool OnPickup, string? GoogleAddress)
{
    public static RosterEntry From(RosterPerson person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return new RosterEntry(person.Id, person.Name, person.Role, person.OnPickup, person.GoogleAddress);
    }
}

/// <summary>The roster as both installs read it: the people who can be read, and the records that could not.</summary>
public sealed record RosterView(IReadOnlyList<RosterEntry> Entries, IReadOnlyList<UnreadableRecord> Unreadable)
{
    public IReadOnlyList<RosterEntry> Adults => Entries.Where(entry => entry.Role == MemberRole.Adult).ToList();

    public IReadOnlyList<RosterEntry> Children => Entries.Where(entry => entry.Role == MemberRole.Child).ToList();

    /// <summary>The adults who do the school and nursery run.</summary>
    public IReadOnlyList<RosterEntry> PickupAdults => Entries.Where(entry => entry.OnPickup).ToList();

    /// <summary>The live people of the document, in key order. Reading is allowed on either install.</summary>
    public static RosterView From(HouseholdDocument document)
    {
        var set = HouseholdRecords.Read(document, RosterMaps.Person);
        return new RosterView(set.Items.Select(RosterEntry.From).ToList(), set.Unreadable);
    }
}

/// <summary>The plain line a list row carries for a person. Names who they are; nothing private.</summary>
public static class RosterLabel
{
    public static string For(RosterEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var role = entry.Role == MemberRole.Child ? "child" : "adult";
        return entry.OnPickup ? $"{entry.Name}, {role}, on pickup" : $"{entry.Name}, {role}";
    }
}
