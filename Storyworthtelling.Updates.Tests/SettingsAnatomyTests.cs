namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// One Settings anatomy in all nine apps (audit W8.4b, report 02 §9): folding groups in the order
/// Connections, Vault, the app's own, Diagnostics, then the build line under them.
/// </summary>
public class SettingsAnatomyTests
{
    [Fact]
    public void TheSharedGroupsFrameTheAppsOwn()
    {
        Assert.Equal(
            ["Connections", "Vault", "Shelves", "Power", "Diagnostics"],
            SettingsAnatomy.Arrange(hasConnections: true, ["Shelves", "Power"]));
    }

    [Fact]
    public void AnAppWithNothingToConnectStartsAtTheVault() =>
        Assert.Equal(["Vault", "Library", "Diagnostics"], SettingsAnatomy.Arrange(hasConnections: false, ["Library"]));

    [Fact]
    public void AnAppGroupMayNotTakeASharedName() =>
        Assert.Throws<ArgumentException>(() => SettingsAnatomy.Arrange(true, ["Vault"]));

    [Theory]
    [InlineData(new[] { "Connections", "Vault", "Library", "Diagnostics" }, true)]
    [InlineData(new[] { "Vault", "Diagnostics" }, true)]
    [InlineData(new[] { "Vault", "Connections", "Diagnostics" }, false)]
    [InlineData(new[] { "Connections", "Vault", "Diagnostics", "Library" }, false)]
    [InlineData(new[] { "Library", "Vault", "Diagnostics" }, false)]
    [InlineData(new[] { "Connections", "Library", "Diagnostics" }, false)]
    [InlineData(new[] { "Connections", "Vault", "Library" }, false)]
    public void OnlyTheSuitesOrderPasses(string[] drawn, bool expected) =>
        Assert.Equal(expected, SettingsAnatomy.IsInOrder(drawn));
}
