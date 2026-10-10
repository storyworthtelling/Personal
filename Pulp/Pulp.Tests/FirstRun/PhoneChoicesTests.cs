using Pulp.Domain.Core;
using Pulp.Domain.FirstRun;
using Pulp.Domain.People;

namespace Pulp.Tests.FirstRun;

/// <summary>
/// "This phone is …" offers the adults on the roster and nobody else (D97): a child's page is
/// Favorites (D213), and <see cref="ThisPhone"/> refuses to record a child, so offering one would
/// be a button that throws. Invented household.
/// </summary>
public class PhoneChoicesTests
{
    private static readonly HouseholdRoster Roster = new(
    [
        new HouseholdViewer(new ViewerId("rowan"), IsChild: false),
        new HouseholdViewer(new ViewerId("pip"), IsChild: true),
        new HouseholdViewer(new ViewerId("juniper-vale"), IsChild: false),
    ]);

    [Fact]
    public void OnlyTheAdultsAreOffered() =>
        Assert.Equal(["rowan", "juniper-vale"], PhoneChoices.For(Roster).Select(choice => choice.Id.Value));

    [Fact]
    public void EachChoiceReadsAsAName() =>
        Assert.Equal(["Rowan", "Juniper Vale"], PhoneChoices.For(Roster).Select(choice => choice.Name));

    [Fact]
    public void WithNoRosterThereIsNobodyToOffer() => Assert.Empty(PhoneChoices.For(null));

    [Fact]
    public void EveryChoiceCanBeRecorded()
    {
        var choices = PhoneChoices.For(Roster);

        Assert.NotEmpty(choices);
        Assert.All(choices, choice => Assert.True(ThisPhone.CanBeRecorded(Roster, choice.Id)));
    }
}
