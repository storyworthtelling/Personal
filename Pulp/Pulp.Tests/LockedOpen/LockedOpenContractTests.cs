using System.Text.RegularExpressions;
using Pulp.Domain.LockedOpen;
using Pulp.Domain.Services;

namespace Pulp.Tests.LockedOpen;

/// <summary>
/// Guard G19, Pulp's half. Flyleaf's receiver answers to a package, component, action and permission
/// nobody checks at run time: a drifted name drops the broadcast, the title never opens locked, and
/// nothing says why. Every wire name here is read out of <c>LockedOpen.cs</c> in the launcher's Domain
/// by content, never retyped, so a change on either side fails here. The streaming catalogue Flyleaf
/// will lock-task is also read by content and must equal Pulp's own.
/// </summary>
public class LockedOpenContractTests
{
    private const string FlyleafContract = "FocusPhone/Codex.Domain/Apps/LockedOpen.cs";
    private const string CourierPath = "Pulp.Android/LockedOpen/LockedOpenCourier.cs";

    [Theory]
    [InlineData("Package", nameof(LockedOpenHandOff.Package))]
    [InlineData("Receiver", nameof(LockedOpenHandOff.Receiver))]
    [InlineData("Action", nameof(LockedOpenHandOff.Action))]
    [InlineData("Permission", nameof(LockedOpenHandOff.Permission))]
    [InlineData("PackageExtra", nameof(LockedOpenHandOff.PackageExtra))]
    [InlineData("LinkExtra", nameof(LockedOpenHandOff.LinkExtra))]
    public void EveryWireNameEqualsFlyleafsByteForByte(string flyleafName, string ourName)
    {
        var ours = (string)typeof(LockedOpenHandOff).GetField(ourName)!.GetValue(null)!;

        Assert.Equal(FlyleafConstant(flyleafName), ours);
    }

    [Fact]
    public void TheReceiverActionAndPermissionLiveUnderTheLaunchersPackage()
    {
        Assert.StartsWith(LockedOpenHandOff.Package + ".", LockedOpenHandOff.Receiver, StringComparison.Ordinal);
        Assert.StartsWith(LockedOpenHandOff.Package + ".", LockedOpenHandOff.Action, StringComparison.Ordinal);
        Assert.StartsWith(LockedOpenHandOff.Package + ".", LockedOpenHandOff.Permission, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMaxLinkLengthEqualsFlyleafs()
    {
        var text = File.ReadAllText(Path.Combine(PulpSource.Root(), "..", FlyleafContract));
        var match = Regex.Match(text, @"public const int MaxLinkLength\s*=\s*(\d+)\s*;");

        Assert.True(match.Success, "Could not find LockedOpenHandoff.MaxLinkLength.");
        Assert.Equal(int.Parse(match.Groups[1].Value), LockedOpenHandOff.MaxLinkLength);
    }

    /// <summary>Flyleaf refuses a package its own catalogue does not name; Pulp must never send one it would refuse.</summary>
    [Fact]
    public void EveryPackageOfEveryPublicServiceIsInFlyleafsCatalogue()
    {
        var flyleaf = FlyleafCataloguePackages();
        var ours = ServiceCatalogue.Public.All.SelectMany(service => service.Packages).ToList();

        Assert.NotEmpty(flyleaf);
        Assert.NotEmpty(ours);
        Assert.Equal(ours.Order(StringComparer.Ordinal), flyleaf.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheCourierAimsAtTheNamedComponentAndRequiresThePermission()
    {
        var courier = CourierSource();

        Assert.Contains("new ComponentName(LockedOpenHandOff.Package, LockedOpenHandOff.Receiver)", courier, StringComparison.Ordinal);
        Assert.Contains("new Intent(LockedOpenHandOff.Action)", courier, StringComparison.Ordinal);
        Assert.Contains("SendBroadcast(intent, LockedOpenHandOff.Permission)", courier, StringComparison.Ordinal);
        Assert.Contains("LockedOpenHandOff.Extras(", courier, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCourierNeverBroadcastsToAnyone()
    {
        var courier = CourierSource();

        Assert.DoesNotContain("SendBroadcast(intent)", courier, StringComparison.Ordinal);
        Assert.DoesNotContain("SetPackage(", courier, StringComparison.Ordinal);
    }

    [Fact]
    public void PulpDeclaresItUsesFlyleafsPermission()
    {
        Assert.Matches(@"\[assembly: UsesPermission\(LockedOpenHandOff\.Permission\)\]", CourierSource());
    }

    /// <summary>
    /// Since Android 11 a package the app has not declared it can see is invisible, and an invisible
    /// Flyleaf reads as "not installed": every title would quietly take the plain hand-off.
    /// </summary>
    [Fact]
    public void PulpCanSeeFlyleaf()
    {
        var path = Path.Combine(PulpSource.Root(), "Pulp.Android", "Properties", "AndroidManifest.xml");
        Assert.True(File.Exists(path), "Pulp.Android/Properties/AndroidManifest.xml was not found.");

        Assert.Contains($"<package android:name=\"{LockedOpenHandOff.Package}\"", File.ReadAllText(path), StringComparison.Ordinal);
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
        Assert.True(match.Success, $"Could not find LockedOpenHandoff.{name} in {FlyleafContract}.");
        return match.Groups[1].Value;
    }

    private static List<string> FlyleafCataloguePackages()
    {
        var text = File.ReadAllText(Path.Combine(PulpSource.Root(), "..", FlyleafContract));
        var start = text.IndexOf("public static IReadOnlySet<string> Packages", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not find LockedOpenCatalogue.Packages.");
        var block = text[start..text.IndexOf("};", start, StringComparison.Ordinal)];
        return Regex.Matches(block, @"""([A-Za-z0-9_.]+)""").Select(match => match.Groups[1].Value).ToList();
    }
}
