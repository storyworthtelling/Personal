using System.Text.RegularExpressions;
using Pulp.Domain.Hidden;

namespace Pulp.Tests.Hidden;

/// <summary>
/// Guard G17, Pulp's half. Flyleaf's receiver (the other half) answers to a package, component,
/// action and permission nobody checks at run time: a drifted name drops the broadcast, the streaming
/// apps stay in Flyleaf's tray, and nothing says why. Every wire name here is read out of
/// <c>HiddenAppsHandoff.cs</c> in the launcher's Domain by content, never retyped, so a change on
/// either side fails here.
/// </summary>
public class HiddenAppsContractTests
{
    private const string FlyleafContract = "FocusPhone/Codex.Domain/Apps/HiddenApps.cs";
    private const string CourierPath = "Pulp.Android/Hidden/HiddenAppsCourier.cs";

    [Theory]
    [InlineData("Package", nameof(HiddenAppsHandOff.Package))]
    [InlineData("Receiver", nameof(HiddenAppsHandOff.Receiver))]
    [InlineData("Action", nameof(HiddenAppsHandOff.Action))]
    [InlineData("Permission", nameof(HiddenAppsHandOff.Permission))]
    [InlineData("PackagesExtra", nameof(HiddenAppsHandOff.PackagesExtra))]
    public void EveryWireNameEqualsFlyleafsByteForByte(string flyleafName, string ourName)
    {
        var ours = (string)typeof(HiddenAppsHandOff).GetField(ourName)!.GetValue(null)!;

        Assert.Equal(FlyleafConstant(flyleafName), ours);
    }

    [Fact]
    public void TheReceiverAndActionLiveUnderTheLaunchersPackage()
    {
        Assert.StartsWith(HiddenAppsHandOff.Package + ".", HiddenAppsHandOff.Receiver, StringComparison.Ordinal);
        Assert.StartsWith(HiddenAppsHandOff.Package + ".", HiddenAppsHandOff.Action, StringComparison.Ordinal);
        Assert.StartsWith(HiddenAppsHandOff.Package + ".", HiddenAppsHandOff.Permission, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCourierAimsAtTheNamedComponentAndRequiresThePermission()
    {
        var courier = CourierSource();

        Assert.Contains("new ComponentName(HiddenAppsHandOff.Package, HiddenAppsHandOff.Receiver)", courier, StringComparison.Ordinal);
        Assert.Contains("new Intent(HiddenAppsHandOff.Action)", courier, StringComparison.Ordinal);
        Assert.Contains("SendBroadcast(intent, HiddenAppsHandOff.Permission)", courier, StringComparison.Ordinal);
        Assert.Contains("HiddenAppsHandOff.Extras(", courier, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCourierNeverBroadcastsToAnyone()
    {
        var courier = CourierSource();

        Assert.DoesNotContain("SendBroadcast(intent)", courier, StringComparison.Ordinal);
        Assert.DoesNotContain("SetPackage(", courier, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sender must itself hold the signature permission the receiver demands, or Android drops the
    /// broadcast without a word.
    /// </summary>
    [Fact]
    public void PulpDeclaresItUsesFlyleafsPermission()
    {
        var courier = CourierSource();

        Assert.Matches(@"\[assembly: UsesPermission\(HiddenAppsHandOff\.Permission\)\]", courier);
    }

    private static string CourierSource()
    {
        var path = Path.Combine(PulpSource.Root(), CourierPath);
        Assert.True(File.Exists(path), $"{CourierPath} was not found.");
        return File.ReadAllText(path);
    }

    private static string FlyleafConstant(string name)
    {
        var path = Path.Combine(PulpSource.Root(), "..", FlyleafContract);
        Assert.True(File.Exists(path), $"{FlyleafContract} not found at {path}; is FocusPhone checked out?");
        var match = Regex.Match(
            File.ReadAllText(path), $@"public const string {Regex.Escape(name)}\s*=\s*""([^""]*)""\s*;");
        Assert.True(match.Success, $"Could not find HiddenAppsHandoff.{name} in {FlyleafContract}.");
        return match.Groups[1].Value;
    }
}
