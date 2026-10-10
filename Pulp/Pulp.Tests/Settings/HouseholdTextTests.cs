using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.People;
using Pulp.Domain.Settings;

namespace Pulp.Tests.Settings;

/// <summary>The Household page's words: whose phone this is (D97) and each person's line (D88).</summary>
public class HouseholdTextTests
{
    private static readonly ViewerId Sam = new("sam-example");

    [Fact]
    public void AKnownPhoneNamesItsOwner() =>
        Assert.Equal("This phone is Sam's.", HouseholdText.ThisPhone(new ThisPhoneAnswer(ThisPhoneStatus.Known, Sam), _ => "Sam"));

    [Fact]
    public void AnUnmatchedPhoneSaysTheSharedSentence() =>
        Assert.Equal(ThisPhone.UnmatchedSentence, HouseholdText.ThisPhone(new ThisPhoneAnswer(ThisPhoneStatus.Unmatched, null), _ => "x"));

    [Fact]
    public void NoRosterAsksForThePeopleFirst() =>
        Assert.Equal("Add the people in the household first.", HouseholdText.ThisPhone(new ThisPhoneAnswer(ThisPhoneStatus.NoRoster, null), _ => "x"));

    [Fact]
    public void APersonLineSaysAdultOrChildAndTheBand()
    {
        Assert.Equal("Adult", HouseholdText.Person(isChild: false, ceiling: null));
        Assert.Equal("Child · 7 to 9", HouseholdText.Person(isChild: true, ceiling: AgeBand.SevenToNine));
        Assert.Equal("Child", HouseholdText.Person(isChild: true, ceiling: null));
    }

    [Fact]
    public void TheUndoNamesWhoWasRemoved() => Assert.Equal("Undo remove, Robin.", HouseholdText.RemoveUndo("Robin"));

    /// <summary>Every undo on the page reads like the action card's (§13): "Undo &lt;act&gt;, &lt;what&gt;."</summary>
    [Theory]
    [InlineData("unblock", "Lantern", "Undo unblock, Lantern.")]
    [InlineData("allow", "Orchard", "Undo allow, Orchard.")]
    public void EveryUndoReadsAlike(string act, string what, string expected) => Assert.Equal(expected, HouseholdText.Undo(act, what));

    [Fact]
    public void AnUndoNeedsAnActAndAThing()
    {
        Assert.Throws<ArgumentException>(() => HouseholdText.Undo(" ", "Lantern"));
        Assert.Throws<ArgumentException>(() => HouseholdText.Undo("allow", ""));
    }
}
