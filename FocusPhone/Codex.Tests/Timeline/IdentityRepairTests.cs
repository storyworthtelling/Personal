using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Timeline;

/// <summary>When a message's card offers to separate its address from the correspondence (orphan audit §25).</summary>
public class IdentityRepairTests
{
    [Fact]
    public void AnAddressCanBeSeparatedFromAPersonWithMoreThanOne()
    {
        Assert.True(IdentityRepair.CanSeparate(["5550101", "sam@example.com"], "sam@example.com", isGroup: false));
    }

    [Fact]
    public void APersonWithOneAddressHasNothingToSeparate()
    {
        Assert.False(IdentityRepair.CanSeparate(["5550101"], "5550101", isGroup: false));
    }

    [Fact]
    public void AGroupIsNotOnePersonsAddressesAndIsNeverOffered()
    {
        Assert.False(IdentityRepair.CanSeparate(["5550101", "5550102"], "5550101", isGroup: true));
    }

    [Fact]
    public void AnAddressNotInTheCorrespondenceCannotBeSeparatedFromIt()
    {
        Assert.False(IdentityRepair.CanSeparate(["5550101", "sam@example.com"], "lee@example.com", isGroup: false));
    }
}
