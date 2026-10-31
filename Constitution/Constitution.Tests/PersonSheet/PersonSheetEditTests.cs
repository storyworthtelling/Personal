using Constitution.Domain.Household;
using Constitution.Domain.Settings;
using Constitution.Tests.HouseholdFile;
using Storyworthtelling.Household;

namespace Constitution.Tests.PersonSheet;

public class PersonSheetEditTests
{
    private static readonly RosterEditor Owner = new(RosterRole.Owner);

    private static PersonSheetSave Apply(PersonId id, string name, bool onPickup, string? address, RosterEditor? editor = null, HouseholdDocument? document = null)
    {
        var held = document ?? SheetData.Full();
        return PersonSheetEdit.Apply(editor ?? Owner, held, id, name, onPickup, address, SheetData.After(held), RosterData.OwnerWriter);
    }

    [Fact]
    public void ARenameKeepsTheBirthdateInTheRecord()
    {
        var saved = Apply(SheetData.Robin, "Robin R", false, null);

        Assert.True(saved.Saved);
        var robin = SheetData.People(saved.Document).Single(person => person.Id == SheetData.Robin);
        Assert.Equal("Robin R", robin.Name);
        Assert.Equal(new Birthdate(new DateOnly(1900, 3, 3)), robin.Birthdate);
    }

    [Fact]
    public void PickupAndAddressChangeForAnAdult()
    {
        var saved = Apply(SheetData.Lee, "Lee", true, "lee@example.com");

        var lee = SheetData.People(saved.Document).Single(person => person.Id == SheetData.Lee);
        Assert.True(lee.OnPickup);
        Assert.Equal("lee@example.com", lee.GoogleAddress);
        Assert.Equal(new Birthdate(new DateOnly(1900, 2, 2)), lee.Birthdate);
    }

    [Fact]
    public void AnEmptyAddressClearsIt()
    {
        var saved = Apply(SheetData.Sam, "Sam", true, "  ");

        Assert.Null(SheetData.People(saved.Document).Single(person => person.Id == SheetData.Sam).GoogleAddress);
    }

    [Fact]
    public void ThePartnerIsRefusedAndNothingChanges()
    {
        var before = SheetData.Full();

        var saved = Apply(SheetData.Robin, "Robin R", false, null, new RosterEditor(RosterRole.Partner), before);

        Assert.False(saved.Saved);
        Assert.Equal(RosterRefusalReason.PartnerInstallIsReadOnly, saved.Refusal!.Reason);
        Assert.Same(before, saved.Document);
    }

    [Fact]
    public void WhatIsWrongComesBackAsProblems()
    {
        Assert.Equal([PersonProblem.NameMissing], Apply(SheetData.Robin, "  ", false, null).Problems);
        Assert.Equal([PersonProblem.ChildOnPickup], Apply(SheetData.Robin, "Robin", true, null).Problems);
        Assert.Equal([PersonProblem.AddressShape], Apply(SheetData.Sam, "Sam", true, "not an address").Problems);
        Assert.Equal([PersonProblem.NameTooLong], Apply(SheetData.Sam, new string('x', 81), true, null).Problems);
    }

    [Fact]
    public void ASaveThatFailsLeavesTheDocumentAsItWas()
    {
        var before = SheetData.Full();

        var saved = Apply(SheetData.Robin, "", false, null, document: before);

        Assert.False(saved.Saved);
        Assert.Same(before, saved.Document);
    }

    [Fact]
    public void SomeoneNotThereIsAProblem()
    {
        var saved = Apply(new PersonId("nobody"), "Nobody", false, null);

        Assert.Equal([PersonProblem.NotOnTheRoster], saved.Problems);
    }
}
