using System.Xml.Linq;
using Constitution.Domain;
using Constitution.Domain.Health;

namespace Constitution.Tests.Health;

/// <summary>
/// C-7.1.1 — Constitution can ask Health Connect for access and cannot write to it. Both halves fail
/// silently when wrong: a missing rationale activity means the permission request opens nothing, and a
/// stray WRITE permission makes the app a writer for as long as nobody looks.
///
/// <para>The manifest is read by content, from three places: the head's source attributes
/// (<c>[assembly: UsesPermission]</c>, <c>[Activity]</c>, <c>[IntentFilter]</c>), its hand-written
/// <c>Properties/AndroidManifest.xml</c> (where the activity-alias lives, because no .NET attribute
/// expresses one) and, when a Release build has been made, the <b>merged</b> manifest, which is what the
/// phone actually installs. Each test asserts it read a file, so an empty search cannot pass.</para>
/// </summary>
public class HealthManifestTests
{
    // Typed out on purpose: the guard keeps its own record of the allowed set, so loosening the
    // Domain list alone cannot loosen the guard. Growing it is a deliberate edit here.
    private static readonly string[] ExpectedPermissions =
    [
        "android.permission.health.READ_WEIGHT",
        "android.permission.health.READ_BODY_FAT",
        "android.permission.health.READ_LEAN_BODY_MASS",
        "android.permission.health.READ_RESTING_HEART_RATE",
        "android.permission.health.READ_HEART_RATE_VARIABILITY",
        "android.permission.health.READ_STEPS",
        "android.permission.health.READ_EXERCISE",
        "android.permission.health.READ_SLEEP",
        "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND",
    ];

    private const string ShowRationale = "androidx.health.ACTION_SHOW_PERMISSIONS_RATIONALE";
    private const string ViewUsage = "android.intent.action.VIEW_PERMISSION_USAGE";
    private const string HealthCategory = "android.intent.category.HEALTH_PERMISSIONS";
    private const string SystemOnly = "android.permission.START_VIEW_PERMISSION_USAGE";

    // ---- the rationale activity ----

    [Fact]
    public void DeclaresTheRationaleActivity()
    {
        // 1. The activity itself, from its source attributes: registered name, exported, and the
        //    legacy action Health Connect resolves on older platforms.
        var source = HeadSources().Where(file => file.Text.Contains(
            "Name = ConstitutionIdentity.HealthRationaleActivityName", StringComparison.Ordinal)).ToList();
        Assert.True(source.Count == 1, "Exactly one head class must carry the rationale activity's registered name.");
        Assert.Contains("[Activity(", source[0].Text, StringComparison.Ordinal);
        Assert.Contains("Exported = true", source[0].Text, StringComparison.Ordinal);
        Assert.Contains("[IntentFilter([\"" + ShowRationale + "\"]", source[0].Text, StringComparison.Ordinal);

        // 2. The alias, from the hand-written manifest.
        AssertAlias(HandWrittenManifest(), "Properties/AndroidManifest.xml");
    }

    [Fact]
    public void TheMergedManifestDeclaresItToo()
    {
        var merged = MergedReleaseManifest();
        if (merged is null)
        {
            // No Release build here: the source half above is what runs. CI and the build recipe make one.
            return;
        }

        var activity = HealthManifestReader.Activity(merged, ConstitutionIdentity.HealthRationaleActivityName);
        Assert.True(activity is not null, "The merged Release manifest has no rationale activity.");
        Assert.True(HealthManifestReader.IsExported(activity!), "The rationale activity must be exported for the platform to reach it.");
        Assert.Contains(ShowRationale, HealthManifestReader.Actions(activity!));

        AssertAlias(merged, "the merged Release manifest");
    }

    [Fact]
    public void TheRationaleActivityOpensNothingElse()
    {
        var file = HeadSources().Single(source => source.Path.EndsWith("Health/HealthRationaleActivity.cs", StringComparison.Ordinal));
        foreach (var forbidden in new[] { "StartActivity", "WebView", "Linkify", "new Intent(", "Uri.Parse" })
        {
            Assert.DoesNotContain(forbidden, file.Text, StringComparison.Ordinal);
        }

        // The page prints the Domain sentence rather than a copy of it.
        Assert.Contains("HealthRationale.Sentence", file.Text, StringComparison.Ordinal);
    }

    // ---- read-only, and only the seven signals ----

    [Fact]
    public void ReadOnly()
    {
        var declared = AllDeclaredHealthPermissions(out var filesRead);
        Assert.True(filesRead >= 2, "The guard read fewer than the source and the hand-written manifest.");
        Assert.NotEmpty(declared);

        Assert.DoesNotContain(declared, name => name.Contains("WRITE_", StringComparison.Ordinal));
        Assert.DoesNotContain(declared, name => name.EndsWith("READ_HEALTH_DATA_HISTORY", StringComparison.Ordinal));
        Assert.DoesNotContain(declared, name => name.Contains("MEDICAL_DATA", StringComparison.Ordinal));
        Assert.All(declared, name => Assert.Contains("READ_", name, StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyTheSevenSignalsAndBackground()
    {
        var declared = AllDeclaredHealthPermissions(out _);

        Assert.Empty(declared.Except(ExpectedPermissions));
        Assert.Empty(ExpectedPermissions.Except(SourceHealthPermissions()));
        Assert.Equal(ExpectedPermissions.Order(StringComparer.Ordinal), HealthPermissions.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheMergedManifestIsReadOnlyToo()
    {
        var merged = MergedReleaseManifest();
        if (merged is null)
        {
            return;
        }

        var health = HealthManifestReader.ManifestPermissions(merged)
            .Where(name => name.StartsWith(HealthPermissions.Prefix, StringComparison.Ordinal))
            .ToList();
        Assert.Equal(ExpectedPermissions.Order(StringComparer.Ordinal), health.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void HealthPermissionsAreWrittenAsLiterals()
    {
        // A permission passed by constant reference cannot be read by content; only INTERNET may be.
        var nonLiteral = HeadSources()
            .SelectMany(file => HealthManifestReader.SourceNonLiteralArguments(file.Text))
            .Where(argument => argument != "Android.Manifest.Permission.Internet")
            .ToList();

        Assert.True(nonLiteral.Count == 0, "Declare health permissions as string literals:\n" + string.Join("\n", nonLiteral));
    }

    // ---- the detectors themselves ----

    [Fact]
    public void TheDetectorFiresOnAWritePermission()
    {
        var write = "[assembly: UsesPermission(\"android.permission.health." + "WRITE" + "_WEIGHT\")]";
        var history = "[assembly: UsesPermission(Name = \"android.permission.health." + "READ_HEALTH_DATA" + "_HISTORY\")]";

        var found = HealthManifestReader.SourcePermissions(write + "\n" + history);

        Assert.Equal(2, found.Count);
        Assert.Contains(found, name => name.Contains("WRITE_", StringComparison.Ordinal));
        Assert.Contains(found, name => name.EndsWith("READ_HEALTH_DATA_HISTORY", StringComparison.Ordinal));
        Assert.NotEmpty(found.Except(ExpectedPermissions));
    }

    [Fact]
    public void TheManifestReaderSeesAnAliasAndItsFilter()
    {
        var xml = XDocument.Parse(
            "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\"><application>"
            + "<activity-alias android:name=\"x.Alias\" android:exported=\"true\" android:targetActivity=\"x.Real\">"
            + "<intent-filter><action android:name=\"" + ViewUsage + "\" /><category android:name=\"" + HealthCategory + "\" /></intent-filter>"
            + "</activity-alias></application></manifest>");

        var alias = HealthManifestReader.Alias(xml, "x.Alias");

        Assert.NotNull(alias);
        Assert.True(HealthManifestReader.HasFilter(alias!, ViewUsage, HealthCategory));
        Assert.False(HealthManifestReader.HasFilter(alias!, ViewUsage, "android.intent.category.DEFAULT"));
    }

    // ---- helpers ----

    private static void AssertAlias(XDocument manifest, string where)
    {
        var alias = HealthManifestReader.Alias(manifest, ConstitutionIdentity.PermissionUsageAliasName);
        Assert.True(alias is not null, $"{where} declares no activity-alias {ConstitutionIdentity.PermissionUsageAliasName}.");
        Assert.True(HealthManifestReader.IsExported(alias!), $"{where}: the alias must be exported.");
        Assert.Equal(ConstitutionIdentity.HealthRationaleActivityName,
            (string?)alias!.Attribute(HealthManifestReader.Android + "targetActivity"));
        Assert.True(HealthManifestReader.HasFilter(alias, ViewUsage, HealthCategory),
            $"{where}: the alias needs VIEW_PERMISSION_USAGE with the HEALTH_PERMISSIONS category in one intent filter.");
        Assert.Equal(SystemOnly, (string?)alias.Attribute(HealthManifestReader.Android + "permission"));
    }

    private sealed record Source(string Path, string Text);

    private static List<Source> HeadSources() =>
        ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Android/", StringComparison.Ordinal))
            .Select(file => new Source(ConstitutionSource.Relative(file), File.ReadAllText(file)))
            .ToList();

    private static XDocument HandWrittenManifest()
    {
        var path = Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "Properties", "AndroidManifest.xml");
        Assert.True(File.Exists(path), "Constitution.Android/Properties/AndroidManifest.xml was not found.");
        return XDocument.Load(path);
    }

    private static XDocument? MergedReleaseManifest()
    {
        var path = Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "obj", "Release", "net10.0-android", "AndroidManifest.xml");
        return File.Exists(path) ? XDocument.Load(path) : null;
    }

    private static List<string> SourceHealthPermissions() =>
        HeadSources()
            .SelectMany(file => HealthManifestReader.SourcePermissions(file.Text))
            .Where(name => name.StartsWith(HealthPermissions.Prefix, StringComparison.Ordinal))
            .Distinct()
            .ToList();

    /// <summary>Every health permission named by source, the hand-written manifest and, when present, the merged one.</summary>
    private static List<string> AllDeclaredHealthPermissions(out int filesRead)
    {
        filesRead = 0;
        var names = new List<string>();

        var sources = HeadSources();
        filesRead += sources.Count > 0 ? 1 : 0;
        names.AddRange(SourceHealthPermissions());

        filesRead++;
        names.AddRange(HealthManifestReader.ManifestPermissions(HandWrittenManifest()));

        var merged = MergedReleaseManifest();
        if (merged is not null)
        {
            filesRead++;
            names.AddRange(HealthManifestReader.ManifestPermissions(merged));
        }

        return names
            .Where(name => name.StartsWith(HealthPermissions.Prefix, StringComparison.Ordinal))
            .Distinct()
            .ToList();
    }
}
