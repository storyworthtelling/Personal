using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

// D123: the owner's own order for Accounts — pure arithmetic over a list of ids, so the three moves
// the long-press action card offers are tested without a database.
public class InstitutionOrderTests
{
    private static readonly Guid RedwoodBank = Guid.NewGuid();
    private static readonly Guid MeridianCard = Guid.NewGuid();
    private static readonly Guid HarborAutoFinance = Guid.NewGuid();

    [Fact]
    public void MoveUp_SwapsWithThePreviousEntry()
    {
        var result = InstitutionOrder.MoveUp([RedwoodBank, MeridianCard, HarborAutoFinance], MeridianCard);

        Assert.Equal(
            new[] { (MeridianCard, 0), (RedwoodBank, 1), (HarborAutoFinance, 2) },
            result);
    }

    [Fact]
    public void MoveUp_AtTheTop_DoesNothing()
    {
        var result = InstitutionOrder.MoveUp([RedwoodBank, MeridianCard, HarborAutoFinance], RedwoodBank);

        Assert.Equal(new[] { (RedwoodBank, 0), (MeridianCard, 1), (HarborAutoFinance, 2) }, result);
    }

    [Fact]
    public void MoveDown_SwapsWithTheNextEntry()
    {
        var result = InstitutionOrder.MoveDown([RedwoodBank, MeridianCard, HarborAutoFinance], RedwoodBank);

        Assert.Equal(new[] { (MeridianCard, 0), (RedwoodBank, 1), (HarborAutoFinance, 2) }, result);
    }

    [Fact]
    public void MoveDown_AtTheBottom_DoesNothing()
    {
        var result = InstitutionOrder.MoveDown([RedwoodBank, MeridianCard, HarborAutoFinance], HarborAutoFinance);

        Assert.Equal(new[] { (RedwoodBank, 0), (MeridianCard, 1), (HarborAutoFinance, 2) }, result);
    }

    [Fact]
    public void MoveToBottom_MovesTheEntryPastEveryOther()
    {
        var result = InstitutionOrder.MoveToBottom([RedwoodBank, MeridianCard, HarborAutoFinance], RedwoodBank);

        Assert.Equal(new[] { (MeridianCard, 0), (HarborAutoFinance, 1), (RedwoodBank, 2) }, result);
    }

    [Fact]
    public void AppendNew_JoinsAtTheBottom_NeverReAlphabetized()
    {
        // "Zebra Bank" would sort first alphabetically — appending must not do that; a new bank
        // joins wherever the owner's existing arrangement ends.
        var zebra = Guid.NewGuid();
        var result = InstitutionOrder.AppendNew([RedwoodBank, MeridianCard], zebra);

        Assert.Equal(new[] { (RedwoodBank, 0), (MeridianCard, 1), (zebra, 2) }, result);
    }

    [Fact]
    public void SeedAlphabetically_OrdersByNameIgnoringCase()
    {
        var result = InstitutionOrder.SeedAlphabetically(
            [(HarborAutoFinance, "Harbor Auto Finance"), (RedwoodBank, "Redwood Bank"), (MeridianCard, "meridian card")]);

        Assert.Equal(new[] { (HarborAutoFinance, 0), (MeridianCard, 1), (RedwoodBank, 2) }, result);
    }

    [Fact]
    public void EveryMove_RenumbersDensely_NoGaps()
    {
        var result = InstitutionOrder.MoveUp([RedwoodBank, MeridianCard, HarborAutoFinance], HarborAutoFinance);

        Assert.Equal([0, 1, 2], result.Select(entry => entry.SortOrder).OrderBy(x => x));
    }
}
