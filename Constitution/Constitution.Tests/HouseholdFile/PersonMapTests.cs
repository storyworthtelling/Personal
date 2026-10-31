using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Storyworthtelling.Household;

namespace Constitution.Tests.HouseholdFile;

/// <summary>The typed map for a <c>person</c> record: the fields it writes, and that the birthdate has exactly one place to be.</summary>
public class PersonMapTests
{
    [Fact]
    public void TheKeyIsPersonSlashTheId()
    {
        Assert.Equal("person/sam", RosterMaps.Person.KeyOf(RosterData.Sam()).Value);
        Assert.Equal(ConstitutionKinds.Person, RosterMaps.Person.Kind);
    }

    [Fact]
    public void EveryFieldRoundTrips()
    {
        foreach (var person in new[] { RosterData.Sam(), RosterData.Lee(), RosterData.Robin(), RosterData.Juniper() })
        {
            var document = HouseholdRecords.Put(HouseholdDocument.Empty, RosterMaps.Person, person, RosterData.At(1), RosterData.OwnerWriter);

            var read = Assert.Single(HouseholdRecords.Read(document, RosterMaps.Person).Items);

            Assert.Equal(person, read);
        }
    }

    [Fact]
    public void TheBirthdateIsInItsOwnFieldAndNowhereElse()
    {
        var fields = RosterMaps.Person.FieldsOf(RosterData.Sam());

        Assert.Equal("1900-01-01", fields["birthdate"]);
        Assert.All(fields.Where(field => field.Key != "birthdate"),
            field => Assert.DoesNotContain(RosterData.FakeYear, field.Value, StringComparison.Ordinal));
        Assert.All(fields.Keys, key => Assert.DoesNotContain(RosterData.FakeYear, key, StringComparison.Ordinal));
    }

    [Fact]
    public void NoBirthdateIsAnEmptyFieldSoAnEditClearsIt()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, RosterMaps.Person, RosterData.Sam(), RosterData.At(1), RosterData.OwnerWriter);

        var cleared = HouseholdRecords.Put(document, RosterMaps.Person, RosterData.Sam() with { Birthdate = null }, RosterData.At(2), RosterData.OwnerWriter);

        Assert.Null(Assert.Single(HouseholdRecords.Read(cleared, RosterMaps.Person).Items).Birthdate);
        Assert.Equal(string.Empty, cleared.Live(RosterMaps.Person.KeyOf(RosterData.Sam()))!.Fields["birthdate"]);
    }

    [Fact]
    public void ARoleWordThisBuildDoesNotKnowIsUnreadableNotGuessed()
    {
        var odd = HouseholdRecord.Write(
            new RecordKey(ConstitutionKinds.Person, "pat"), 9,
            new Dictionary<string, string> { ["person"] = "pat", ["name"] = "Pat", ["role"] = "guardian", ["pickup"] = "false" },
            RosterData.At(1), RosterData.OwnerWriter);

        var set = HouseholdRecords.Read(HouseholdDocument.Empty.With(odd), RosterMaps.Person);

        Assert.Empty(set.Items);
        Assert.Contains("role", Assert.Single(set.Unreadable).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RolesAreStoredAsWordsNotEnumNames()
    {
        Assert.Equal("adult", RosterMaps.Person.FieldsOf(RosterData.Sam())["role"]);
        Assert.Equal("child", RosterMaps.Person.FieldsOf(RosterData.Robin())["role"]);
    }
}
