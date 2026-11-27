using Watch.Domain.Dash;

namespace Watch.Tests.Dash;

/// <summary>
/// Which phone the wrist asks (W-5.1.1, R3): the one carrying Post's relay that is near, over Bluetooth, before one
/// reached through the cloud; none at all is said as a quiet line rather than waited for.
/// </summary>
public class RelayNodesTests
{
    [Fact]
    public void ANearbyPhoneLeads()
    {
        var chosen = RelayNodes.Choose([new RelayNode("cloud", Nearby: false), new RelayNode("bluetooth", Nearby: true)]);

        Assert.Equal("bluetooth", chosen);
    }

    [Fact]
    public void WithNoneNearbyTheCloudWillDo() =>
        Assert.Equal("a1", RelayNodes.Choose([new RelayNode("b2", false), new RelayNode("a1", false)]));

    [Fact]
    public void TheChoiceDoesNotDependOnTheOrder() =>
        Assert.Equal(
            RelayNodes.Choose([new RelayNode("b2", true), new RelayNode("a1", true)]),
            RelayNodes.Choose([new RelayNode("a1", true), new RelayNode("b2", true)]));

    [Fact]
    public void NoPhoneIsNone() => Assert.Null(RelayNodes.Choose([]));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ANodeWithNoIdIsPassedOver(string? id) =>
        Assert.Equal("a1", RelayNodes.Choose([new RelayNode(id, true), new RelayNode("a1", false)]));
}
