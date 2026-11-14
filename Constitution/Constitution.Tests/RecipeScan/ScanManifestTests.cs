using System.Text.RegularExpressions;
using System.Xml.Linq;
using Constitution.Domain;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// The scan adds one thing to the manifest: android.permission.CAMERA (asked for at the tap), with the camera features declared not
/// required. Both phones photograph a recipe's pages (D105, D176) and Android refuses a capture intent from an app that declares the
/// permission without holding it, even one aimed at Plate. Plate's old recipe-read permission is gone with its contract (D176). Read from the head's source and, once a Release build has been made, from the merged
/// manifest the phone installs.
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
    public void PlatesRecipeReadPermissionIsNoLongerHeld()
    {
        Assert.DoesNotContain(HeadSources(), text => text.Contains("READ_RECIPE", StringComparison.Ordinal) || text.Contains("RecipeReadContract", StringComparison.Ordinal));
    }

    [Fact]
    public void NoOtherNewPermissionIsDeclaredByTheHead()
    {
        var others = HeadUses()
            .Where(argument => argument != "Android.Manifest.Permission.Internet")
            .Where(argument => !argument.StartsWith("\"android.permission.health.", StringComparison.Ordinal))
            // Index's budget broadcast reaches only an app that holds it (D174).
            .Where(argument => argument != "Name = ConstitutionIdentity.BudgetPermission")
            .Where(argument => argument != "Name = Android.Manifest.Permission.Camera")
            // C-9.1.3: the brief's short list posts as notifications, which Android 13 refuses without it (D155).
            .Where(argument => argument != "\"android.permission.POST_NOTIFICATIONS\"")
            .ToList();

        Assert.True(others.Count == 0, "A permission nobody listed:\n" + string.Join("\n", others));
    }

    [Fact]
    public void TheMergedManifestAddsTheCameraOnly()
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

        Assert.Equal(
            new[] { "android.permission.CAMERA", "android.permission.POST_NOTIFICATIONS", ConstitutionIdentity.BudgetPermission }.Order(StringComparer.Ordinal).ToList(),
            news.Order(StringComparer.Ordinal).ToList());
        Assert.Contains("android.permission.INTERNET", requested);
        Assert.DoesNotContain(requested, name => name.EndsWith(".READ_RECIPE", StringComparison.Ordinal));
        Assert.Equal(1, requested.Count(name => name == "android.permission.CAMERA"));

        // The capture provider: private, grants only the one file the camera is handed, authority from the frozen application id.
        var provider = XDocument.Load(path).Descendants("provider")
            .Single(element => ((string?)element.Attribute(Android + "name"))?.EndsWith("RecipeCaptureProvider", StringComparison.Ordinal) == true);
        Assert.Equal(ConstitutionIdentity.ApplicationId + ".capture", (string?)provider.Attribute(Android + "authorities"));
        Assert.Equal("false", (string?)provider.Attribute(Android + "exported"));
        Assert.Equal("true", (string?)provider.Attribute(Android + "grantUriPermissions"));

        // Without these two a device lacking a camera or its autofocus is refused the install by the store.
        var features = XDocument.Load(path).Descendants("uses-feature")
            .ToDictionary(element => (string)element.Attribute(Android + "name")!, element => (string?)element.Attribute(Android + "required"));
        Assert.Equal("false", features["android.hardware.camera"]);
        Assert.Equal("false", features["android.hardware.camera.autofocus"]);
        Assert.DoesNotContain(features, pair => pair.Value != "false" && pair.Key.StartsWith("android.hardware.camera", StringComparison.Ordinal));
    }

    [Fact]
    public void CameraIsTheOnlyRuntimePermissionTheScanAdds()
    {
        var uses = HeadUses().Where(argument => argument.Contains("Camera", StringComparison.Ordinal)).ToList();

        Assert.Equal("Name = Android.Manifest.Permission.Camera", Assert.Single(uses));
        Assert.DoesNotContain(HeadUses(), argument =>
            argument.Contains("Storage", StringComparison.OrdinalIgnoreCase)
            || argument.Contains("Media", StringComparison.OrdinalIgnoreCase)
            || argument.Contains("RecordAudio", StringComparison.OrdinalIgnoreCase)
            || argument.Contains("Location", StringComparison.OrdinalIgnoreCase));
    }
}
