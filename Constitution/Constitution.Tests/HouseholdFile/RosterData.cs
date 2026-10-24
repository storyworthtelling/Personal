using Constitution.Domain.Household;
using Storyworthtelling.Household;

namespace Constitution.Tests.HouseholdFile;

/// <summary>
/// The invented household (Sam, Lee, Robin, Juniper; example.com). The birthdates are deliberately
/// not birthdays: every one is in 1900, so a leak is recognisable by eye and by <see cref="FakeYear"/>.
/// </summary>
internal static class RosterData
{
    public const string FakeYear = "1900";
    public const string OwnerWriter = "owner-install";
    public const string PartnerWriter = "partner-install";

    public static RosterPerson Sam() =>
        new(new PersonId("sam"), "Sam", MemberRole.Adult, new Birthdate(new DateOnly(1900, 1, 1)), onPickup: true, googleAddress: "sam@example.com");

    public static RosterPerson Lee() =>
        new(new PersonId("lee"), "Lee", MemberRole.Adult, new Birthdate(new DateOnly(1900, 2, 2)), onPickup: false, googleAddress: null);

    public static RosterPerson Robin() =>
        new(new PersonId("robin"), "Robin", MemberRole.Child, new Birthdate(new DateOnly(1900, 3, 3)), onPickup: false, googleAddress: null);

    public static RosterPerson Juniper() =>
        new(new PersonId("juniper"), "Juniper", MemberRole.Child, null, onPickup: false, googleAddress: null);

    public static HlcTimestamp At(long millis, int counter = 0) => new(millis, counter);

    /// <summary>The household as the owner's install would have written it.</summary>
    public static HouseholdDocument Household()
    {
        var document = HouseholdDocument.Empty;
        var editor = new RosterEditor(RosterRole.Owner);
        var tick = 1000L;
        foreach (var person in new[] { Sam(), Lee(), Robin(), Juniper() })
        {
            document = editor.Save(document, person, At(tick++), OwnerWriter).Document;
        }

        return document;
    }
}
