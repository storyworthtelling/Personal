using Constitution.Domain.Household;
using Constitution.Domain.Settings;

namespace Constitution.Tests.HouseholdSettings;

public class HouseholdPersonSheetTests
{
    private static readonly RosterEditor Owner = new(RosterRole.Owner);

    private static HouseholdPersonDraft Draft(string who = "Pat", MemberRole role = MemberRole.Adult, bool pickup = false, string? address = null) =>
        new(null, who, role, pickup, address);

    [Fact]
    public void AValidDraftBecomesAPerson()
    {
        var check = HouseholdPersonSheet.Check(Owner, HouseholdData.Roster(), Draft(address: "pat@example.com"));

        Assert.True(check.CanSave);
        Assert.Equal("pat", check.Person!.Id.Value);
    }

    [Fact]
    public void ANewIdNeverTakesAnExistingOne()
    {
        var check = HouseholdPersonSheet.Check(Owner, HouseholdData.Roster(), Draft("Sam"));

        Assert.Equal("sam-2", check.Person!.Id.Value);
    }

    [Fact]
    public void TheBirthdateRidesAlongAsTheRostersOwnValue()
    {
        var check = HouseholdPersonSheet.Check(Owner, HouseholdData.Roster(), Draft(), new Birthdate(new DateOnly(1900, 4, 4)));

        Assert.Equal(new Birthdate(new DateOnly(1900, 4, 4)), check.Person!.Birthdate);
    }

    [Fact]
    public void AnEditKeepsItsId()
    {
        var draft = Draft("Samuel") with { Id = new PersonId("sam") };

        Assert.Equal("sam", HouseholdPersonSheet.Check(Owner, HouseholdData.Roster(), draft).Person!.Id.Value);
    }

    [Fact]
    public void ProblemsAreTyped()
    {
        Assert.Contains(PersonProblem.NameMissing, HouseholdPersonSheet.Check(Owner, HouseholdData.Roster(), Draft("  ")).Problems);
        Assert.Contains(PersonProblem.NameTooLong, HouseholdPersonSheet.Check(Owner, HouseholdData.Roster(), Draft(new string('a', 81))).Problems);
        Assert.Contains(PersonProblem.ChildOnPickup,
            HouseholdPersonSheet.Check(Owner, HouseholdData.Roster(), Draft(role: MemberRole.Child, pickup: true)).Problems);
        Assert.Contains(PersonProblem.AddressShape,
            HouseholdPersonSheet.Check(Owner, HouseholdData.Roster(), Draft(address: "not an address")).Problems);
    }

    [Fact]
    public void PartnerIsRefusedWithTheReason()
    {
        var check = HouseholdPersonSheet.Check(new RosterEditor(RosterRole.Partner), HouseholdData.Roster(), Draft());

        Assert.False(check.CanSave);
        Assert.Equal(RosterRefusalReason.PartnerInstallIsReadOnly, check.Refusal!.Reason);
    }
}
