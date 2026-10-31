using System.Reflection;
using Constitution.Domain.Household;
using Constitution.Domain.Settings;
using Constitution.Tests.HouseholdFile;
using Storyworthtelling.Household;

namespace Constitution.Tests.PersonSheet;

public class PersonSheetModelTests
{
    private static RosterView Roster() => RosterView.From(RosterData.Household());

    private static PersonSheetModel Sheet(PersonId id, RosterRole role = RosterRole.Owner, RosterView? roster = null) =>
        PersonSheetModel.Build(roster ?? Roster(), id, role)!;

    private static IEnumerable<string> Words(PersonSheetModel model) =>
        new[] { model.Title, model.RoleLine, model.PickupLine, model.CalendarLine, model.ReadOnlyNote, model.KeepNote, model.ToString() }
            .Concat(model.Actions.Select(PersonSheetModel.LabelOf))
            .OfType<string>();

    [Fact]
    public void AnAdultShowsNameRolePickupAndCalendar()
    {
        var sheet = Sheet(SheetData.Sam);

        Assert.Equal("Sam", sheet.Title);
        Assert.Equal("Adult", sheet.RoleLine);
        Assert.Equal("On pickup", sheet.PickupLine);
        Assert.Equal("Calendar shared with sam@example.com", sheet.CalendarLine);
    }

    [Fact]
    public void AnAdultOffPickupSaysSo()
    {
        Assert.Equal("Not on pickup", Sheet(SheetData.Lee).PickupLine);
        Assert.Null(Sheet(SheetData.Lee).CalendarLine);
    }

    [Fact]
    public void AChildHasNoPickupLine()
    {
        var sheet = Sheet(SheetData.Robin);

        Assert.Equal("Child", sheet.RoleLine);
        Assert.Null(sheet.PickupLine);
    }

    [Fact]
    public void TheOwnerCanGoBackRemoveAndSave()
    {
        var sheet = Sheet(SheetData.Robin);

        Assert.True(sheet.CanEdit);
        Assert.Null(sheet.ReadOnlyNote);
        Assert.Equal([PersonSheetAction.Back, PersonSheetAction.Remove, PersonSheetAction.Save], sheet.Actions);
    }

    [Fact]
    public void ThePartnerGetsNoRemoveAndNoSave()
    {
        var sheet = Sheet(SheetData.Robin, RosterRole.Partner);

        Assert.False(sheet.CanEdit);
        Assert.Equal([PersonSheetAction.Back], sheet.Actions);
        Assert.Equal(RosterEditor.PartnerReadOnlySentence, sheet.ReadOnlyNote);
    }

    [Fact]
    public void AnUnknownRoleHasTheOwnersRights()
    {
        var role = RosterEditor.RoleFor(HouseholdFileRole.Unknown);

        Assert.Contains(PersonSheetAction.Remove, Sheet(SheetData.Robin, role).Actions);
    }

    [Fact]
    public void TheLastAdultHasNoRemoveAndSaysWhy()
    {
        var roster = RosterView.From(SheetData.OneAdult());

        var sheet = Sheet(SheetData.Sam, roster: roster);

        Assert.DoesNotContain(PersonSheetAction.Remove, sheet.Actions);
        Assert.Equal(RosterRemoval.LastAdultSentence("Sam"), sheet.KeepNote);
        Assert.Contains(PersonSheetAction.Remove, Sheet(SheetData.Robin, roster: roster).Actions);
    }

    [Fact]
    public void SomeoneNotThereHasNoSheet()
    {
        Assert.Null(PersonSheetModel.Build(Roster(), new PersonId("nobody"), RosterRole.Owner));
    }

    [Fact]
    public void NoBirthdateIsEverInAString()
    {
        foreach (var entry in Roster().Entries)
        {
            foreach (var role in new[] { RosterRole.Owner, RosterRole.Partner })
            {
                foreach (var word in Words(Sheet(entry.Id, role)))
                {
                    Assert.DoesNotContain(RosterData.FakeYear, word);
                    Assert.DoesNotContain("birth", word, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("age ", word, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void TheSheetHasNoMemberThatCouldHoldOne()
    {
        var names = typeof(PersonSheetModel).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.Name);

        Assert.DoesNotContain(names, name => name.Contains("birth", StringComparison.OrdinalIgnoreCase) || name.Contains("age", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(PersonSheetModel).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => property.PropertyType == typeof(Birthdate) || property.PropertyType == typeof(Birthdate?) || property.PropertyType == typeof(DateOnly));
    }

    [Fact]
    public void EveryProblemHasWordsAndNoDate()
    {
        foreach (var problem in Enum.GetValues<PersonProblem>())
        {
            var words = PersonSheetModel.Describe(problem);

            Assert.False(string.IsNullOrWhiteSpace(words), problem.ToString());
            Assert.DoesNotContain("birth", words, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EveryActionHasALabel()
    {
        Assert.Equal("Back", PersonSheetModel.LabelOf(PersonSheetAction.Back));
        Assert.Equal("Remove", PersonSheetModel.LabelOf(PersonSheetAction.Remove));
        Assert.Equal("Save", PersonSheetModel.LabelOf(PersonSheetAction.Save));
    }
}
