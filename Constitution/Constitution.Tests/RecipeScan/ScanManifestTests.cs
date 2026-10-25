using System.Text.RegularExpressions;
using System.Xml.Linq;
using Constitution.Domain;
using Constitution.Domain.Recipes;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// The scan adds exactly one thing to the manifest: Constitution holds Plate's recipe permission. It is a custom permission Plate
/// declares at signature level, not a runtime one, so it is never asked for in a dialog and the INTERNET check is unchanged. Read
/// from the head's source and, once a Release build has been made, from the merged manifest the phone installs.
/// </summary>
public class ScanManifestTests
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    private static readonly Regex Uses = new(@"\[assembly:\s*UsesPermission\(([^)]*)\)\]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>What an AndroidX library merges in by itself (WorkManager and core). Anything beyond these and the head's own is news.</summary>
    private static readonly string[] FromLibraries =
    [
        ConstitutionIdentity.ApplicationId + ".DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION",
        "android.permission.WAKE_LOCK",
        "android.permission.ACCESS_NETWORK_STATE",
        "android.permission.RECEIVE_BOOT_COMPLETED",
        "android.permission.FOREGROUND_SERVICE",
    ];

    private static IEnumerable<string> HeadSources() =>
        ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Android/", StringComparison.Ordinal))
            .Select(File.ReadAllText);

    private static IEnumerable<string> HeadUses() =>
        HeadSources().SelectMany(text => Uses.Matches(text).Select(match => match.Groups[1].Value.Trim()));

    [Fact]
    public void ThePermissionIsPlatesAndNotARuntimeOne()
    {
        Assert.StartsWith(RecipeReadContract.PlatePackage + ".", RecipeReadContract.Permission, StringComparison.Ordinal);
        Assert.DoesNotContain("android.permission.", RecipeReadContract.Permission, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadUsesItByTheContractConstant()
    {
        var mine = HeadUses().Where(argument => argument.Contains("RecipeReadContract.Permission", StringComparison.Ordinal)).ToList();

        Assert.Equal("Name = RecipeReadContract.Permission", Assert.Single(mine));
    }

    [Fact]
    public void ConstitutionHoldsItAndNeverDeclaresOrAsksForIt()
    {
        var sources = HeadSources().ToList();

        Assert.DoesNotContain(sources, text => text.Contains("[assembly: Permission(", StringComparison.Ordinal) && text.Contains("RecipeReadContract", StringComparison.Ordinal));
        Assert.DoesNotContain(sources, text => text.Contains("RequestPermissions", StringComparison.Ordinal) && text.Contains("RecipeReadContract", StringComparison.Ordinal));
    }

    [Fact]
    public void NoOtherNewPermissionIsDeclaredByTheHead()
    {
        var others = HeadUses()
            .Where(argument => argument != "Android.Manifest.Permission.Internet")
            .Where(argument => !argument.StartsWith("\"android.permission.health.", StringComparison.Ordinal))
            .Where(argument => argument != "Name = RecipeReadContract.Permission")
            .ToList();

        Assert.True(others.Count == 0, "A permission nobody listed:\n" + string.Join("\n", others));
    }

    [Fact]
    public void TheMergedManifestAddsOnlyPlatesPermission()
    {
        var path = Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "obj", "Release", "net10.0-android", "android", "AndroidManifest.xml");
        if (!File.Exists(path))
        {
            // No Release build here: the source half above is what runs. The recipe makes one and reads this file.
            return;
        }

        var requested = XDocument.Load(path).Descendants("uses-permission")
            .Select(element => (string?)element.Attribute(Android + "name"))
            .OfType<string>()
            .ToList();
        var news = requested
            .Where(name => name != "android.permission.INTERNET")
            .Where(name => !name.StartsWith("android.permission.health.", StringComparison.Ordinal))
            .Where(name => !FromLibraries.Contains(name))
            .ToList();

        Assert.Equal([RecipeReadContract.Permission], news);
        Assert.Contains("android.permission.INTERNET", requested);
        Assert.Equal(1, requested.Count(name => name == RecipeReadContract.Permission));
    }
}
