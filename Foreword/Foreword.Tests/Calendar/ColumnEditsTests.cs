using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// Setting up the columns in Foreword: add, rename, reorder, remove, and say who has an account.
///
/// <para>A column's name is also how every rule refers to it — a calendar mapped to it, the words
/// that route to it, the addresses invited as it, a work shift drawn in it. So an edit to the name is
/// an edit to all of those, or the rules quietly point at a column that no longer exists.</para>
/// </summary>
public class ColumnEditsTests
{
    private static (CalendarMappingOptions Mapping, WorkScheduleOptions Work) Setup()
    {
        var mapping = new CalendarMappingOptions
        {
            Columns =
            [
                new ColumnChoice { Name = "Alex", HasAccount = true },
                new ColumnChoice { Name = "Riley" },
            ],
            CalendarColumns = { ["alex@example.com"] = Household.Alex, ["r@group.calendar.google.com"] = Household.Riley },
            Keywords = { [Household.Riley] = ["Riley"] },
            PersonAddresses = { [Household.Alex] = ["alex@example.com"] },
            DefaultColumn = Household.Alex,
        };
        var work = new WorkScheduleOptions
        {
            Shifts = [new WorkShift { Person = Household.Alex, Start = "09:00", End = "17:00" }],
        };

        return (mapping, work);
    }

    [Fact]
    public void AddingAColumnPutsItLast()
    {
        var (mapping, _) = Setup();

        Assert.True(ColumnEdits.Add(mapping, "Jo", hasAccount: false));

        Assert.Equal([Household.Alex, Household.Riley, Household.Jo], mapping.Layout().People);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("alex")]
    [InlineData("Watch")]
    public void ANameThatIsEmptyTakenOrWatchIsRefused(string name)
    {
        var (mapping, _) = Setup();

        Assert.False(ColumnEdits.Add(mapping, name, hasAccount: false));
        Assert.Equal(2, mapping.Columns.Count);
    }

    [Fact]
    public void RenamingAColumnCarriesEveryRuleThatNamedIt()
    {
        var (mapping, work) = Setup();

        Assert.True(ColumnEdits.Rename(mapping, work, Household.Alex, "Sam"));

        Assert.Equal([Household.Sam, Household.Riley], mapping.Layout().People);
        Assert.Equal(Household.Sam, mapping.CalendarColumns["alex@example.com"]);
        Assert.Equal(["alex@example.com"], mapping.PersonAddresses[Household.Sam]);
        Assert.False(mapping.PersonAddresses.ContainsKey(Household.Alex));
        Assert.Equal(Household.Sam, mapping.DefaultColumn);
        Assert.Equal(Household.Sam, work.Shifts[0].Person);
    }

    [Fact]
    public void RenamingOntoAnotherColumnIsRefused()
    {
        var (mapping, work) = Setup();

        Assert.False(ColumnEdits.Rename(mapping, work, Household.Alex, "riley"));
        Assert.Equal(Household.Alex, mapping.CalendarColumns["alex@example.com"]);
    }

    /// <summary>
    /// A calendar that fed a removed column is not silently re-pointed somewhere else: it becomes
    /// unmapped, which Foreword already holds and names on the page (D34) until it is answered.
    /// </summary>
    [Fact]
    public void RemovingAColumnLeavesItsCalendarsToBeAnsweredAgain()
    {
        var (mapping, work) = Setup();

        Assert.True(ColumnEdits.Remove(mapping, work, Household.Alex));

        Assert.Equal([Household.Riley], mapping.Layout().People);
        Assert.False(mapping.CalendarColumns.ContainsKey("alex@example.com"));
        Assert.False(mapping.PersonAddresses.ContainsKey(Household.Alex));
        Assert.Null(mapping.DefaultColumn);
        Assert.Empty(work.Shifts);
    }

    [Fact]
    public void TheLastColumnCannotBeRemoved()
    {
        var (mapping, work) = Setup();
        ColumnEdits.Remove(mapping, work, Household.Alex);

        Assert.False(ColumnEdits.Remove(mapping, work, Household.Riley));
        Assert.Single(mapping.Columns);
    }

    [Fact]
    public void MovingAColumnChangesWhereItPrints()
    {
        var (mapping, _) = Setup();

        Assert.True(ColumnEdits.Move(mapping, Household.Riley, -1));
        Assert.Equal([Household.Riley, Household.Alex], mapping.Layout().People);

        Assert.False(ColumnEdits.Move(mapping, Household.Riley, -1));
    }

    [Fact]
    public void SayingSomebodyHasNoAccountDropsTheAddressesThatCouldNeverMatch()
    {
        var (mapping, _) = Setup();

        ColumnEdits.SetHasAccount(mapping, Household.Alex, false);

        Assert.Empty(mapping.Layout().WithAccounts);
        Assert.False(mapping.PersonAddresses.ContainsKey(Household.Alex));
    }

    /// <summary>
    /// The work blocks a fresh install starts with belong to the people with accounts, whoever they
    /// turn out to be — not to two names written into the defaults.
    /// </summary>
    [Fact]
    public void DefaultWorkShiftsGoToTheFirstTwoPeopleWithAccounts()
    {
        var shifts = WorkScheduleOptions.Defaults(Household.Layout).Shifts;

        Assert.Equal([Household.Alex, Household.Sam], shifts.Select(s => s.Person));
    }
}
