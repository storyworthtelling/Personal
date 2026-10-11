using Codex.Domain.Apps;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Apps;

/// <summary>
/// The set of apps Pulp asks Flyleaf to keep out of its tray (D8, D92, D93): the codec the receiver
/// and the store share, and the tray rule that honours it. Invented package names throughout.
/// </summary>
public class HiddenAppsTests
{
    private static readonly CategoryCatalog Catalog = CategoryCatalog.Seed();
    private static readonly IReadOnlyDictionary<string, string> NoOverrides = new Dictionary<string, string>();

    [Fact]
    public void AbsentPayloadIsEmpty()
    {
        Assert.True(HiddenAppsCodec.Decode(null).IsEmpty);
        Assert.True(HiddenAppsCodec.Decode("").IsEmpty);
        Assert.False(HiddenAppsCodec.TryDecode(null, out _));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    [InlineData("[1,2]")]
    [InlineData("[\"com.example.one\", 7]")]
    [InlineData("[\"com.example.one\"")]
    public void GarbledPayloadIsEmptyAndNotAccepted(string garbled)
    {
        Assert.True(HiddenAppsCodec.Decode(garbled).IsEmpty);
        Assert.False(HiddenAppsCodec.TryDecode(garbled, out _));
    }

    [Fact]
    public void AValidEmptyListIsAcceptedAndMeansUnhideEverything()
    {
        Assert.True(HiddenAppsCodec.TryDecode("[]", out var set));
        Assert.True(set.IsEmpty);
    }

    [Fact]
    public void RoundTripKeepsTheSetSortedAndDistinct()
    {
        var set = HiddenApps.Of(["com.example.zeta", "com.example.alpha", "com.example.alpha", " com.example.mid "]);
        var json = HiddenAppsCodec.Encode(set);

        Assert.True(HiddenAppsCodec.TryDecode(json, out var back));
        Assert.Equal(["com.example.alpha", "com.example.mid", "com.example.zeta"], back.Packages);
    }

    [Fact]
    public void TheOwnersOwnAppsAndNonPackagesCanNeverBeHidden()
    {
        var set = HiddenApps.Of(
        [
            PolicyContext.PulpPackage, PolicyContext.CommunicationPackage, PolicyContext.LauncherPackage,
            PolicyContext.IndexPackage, "", "  ", "no-dots", "com..bad", "com.example.good",
        ]);

        Assert.Equal(["com.example.good"], set.Packages);
    }

    [Fact]
    public void ContainsIsExactAndCaseSensitive()
    {
        var set = HiddenApps.Of(["com.example.stream"]);
        Assert.True(set.Contains("com.example.stream"));
        Assert.False(set.Contains("com.example.stream.kids"));
        Assert.False(set.Contains("COM.EXAMPLE.STREAM"));
    }

    [Fact]
    public void TheTrayDropsHiddenAppsEverywhere()
    {
        var apps = new[]
        {
            new TrayApp("com.example.stream", 4),
            new TrayApp("com.example.other", 4),
            new TrayApp("com.example.loose", -1),
        };
        var hidden = HiddenApps.Of(["com.example.stream"]);

        var model = AppTrayLayout.Build(apps, NoOverrides, Catalog, hidden);

        var all = model.RepoApps.Concat(model.Folders.SelectMany(f => f.Apps)).Concat(model.LooseApps)
            .Select(a => a.PackageName).ToList();
        Assert.DoesNotContain("com.example.stream", all);
        Assert.Contains("com.example.other", all);
        Assert.Contains("com.example.loose", all);
    }

    [Fact]
    public void AnOverrideDoesNotBringAHiddenAppBack()
    {
        var overrides = new Dictionary<string, string> { ["com.example.stream"] = CatalogFirstId() };
        var model = AppTrayLayout.Build([new TrayApp("com.example.stream", -1)], overrides, Catalog,
            HiddenApps.Of(["com.example.stream"]));

        Assert.Empty(model.Folders);
        Assert.Empty(model.LooseApps);
        Assert.Empty(model.RepoApps);
    }

    [Fact]
    public void NoHiddenSetLeavesTheTrayAsItWas()
    {
        var apps = new[] { new TrayApp("com.example.stream", -1) };
        var without = AppTrayLayout.Build(apps, NoOverrides, Catalog);
        var withEmpty = AppTrayLayout.Build(apps, NoOverrides, Catalog, HiddenApps.None);

        Assert.Equal(without.LooseApps, withEmpty.LooseApps);
        Assert.Single(without.LooseApps);
    }

    [Fact]
    public void PulpIsASiblingBookOnTheShelfNotATrayApp()
    {
        // D91: cover-out like Foreword and Index. The owner prefix already makes it a repo app.
        var model = AppTrayLayout.Build([new TrayApp(PolicyContext.PulpPackage, -1)], NoOverrides, Catalog,
            HiddenApps.Of(["com.example.stream"]));

        Assert.Equal([PolicyContext.PulpPackage], model.RepoApps.Select(a => a.PackageName));
        Assert.Empty(model.LooseApps);
    }

    private static string CatalogFirstId() => Catalog.Categories[0].Id;
}
