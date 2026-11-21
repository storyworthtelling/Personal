using System.Text.RegularExpressions;
using Storyworthtelling.Updates;
using Watch.Domain;

namespace Watch.Tests.Guard;

/// <summary>
/// Plan §4: the watch's frozen surface. Nothing fails at build time if one of these moves, and nothing fails loudly at
/// run time either: the Data Layer delivers only between apps of one package and one signature, a listener hears only
/// the paths it names, the platform remembers a component by its registered name, and a stored preference is found by
/// its key. Move any of them and the phone's update simply never reaches the watch. These tests fail instead.
///
/// <para>The expected values are typed out as literals on purpose: the guard exists to notice a value moving, so it
/// carries its own record of what the value was. Adding a path, a component or a key is a deliberate edit here.</para>
/// </summary>
public class WatchInvariantsTests
{
    private static readonly string[] ExpectedPackages =
    [
        "com.storyworthtelling.focusphone.communication",
        "com.storyworthtelling.focusphone.audio",
        "com.storyworthtelling.constitution",
        "com.storyworthtelling.focusphone.launcher",
        "com.storyworthtelling.watch.point",
    ];

    private static readonly string[] ExpectedInstallerComponents =
    [
        "com.storyworthtelling.focusphone.launcher.point.UpdateListenerService",
        "com.storyworthtelling.focusphone.launcher.point.InstallStatusReceiver",
        "com.storyworthtelling.focusphone.launcher.point.ReplacedReceiver",
    ];

    private static readonly string[] ExpectedInstallerPermissions =
    [
        "android.permission.REQUEST_INSTALL_PACKAGES",
        "android.permission.UPDATE_PACKAGES_WITHOUT_USER_ACTION",
        "android.permission.POST_NOTIFICATIONS",
    ];

    private static string Installer(params string[] parts) =>
        Path.Combine([WatchFiles.RepoRoot(), "Watch", "Point.Installer", .. parts]);

    private static string Text(string path)
    {
        Assert.True(File.Exists(path), $"{Path.GetRelativePath(WatchFiles.RepoRoot(), path)} isn’t there.");
        return File.ReadAllText(path);
    }

    private static IEnumerable<string> InstallerSources() =>
        Directory.Exists(Installer())
            ? Directory.EnumerateFiles(Installer(), "*.cs", SearchOption.AllDirectories).Where(path => !WatchFiles.IsBuildOutput(path))
            : [];

    // ---- the packages (plan §2.2, PC1) ----

    [Fact]
    public void PackagesAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.focusphone.communication", WatchIdentity.DashPackage);
        Assert.Equal("com.storyworthtelling.focusphone.audio", WatchIdentity.WavePackage);
        Assert.Equal("com.storyworthtelling.constitution", WatchIdentity.BeatPackage);
        Assert.Equal("com.storyworthtelling.focusphone.launcher", WatchIdentity.InstallerPackage);
        Assert.Equal("com.storyworthtelling.watch.point", WatchIdentity.FacePackage);
        Assert.Equal(ExpectedPackages, WatchIdentity.Packages);
    }

    [Fact]
    public void EveryPackageIsTheSuites()
    {
        Assert.NotEmpty(WatchIdentity.Packages);
        Assert.All(WatchIdentity.Packages, package => Assert.True(SuiteApps.IsOwnApp(package), package));
    }

    [Fact]
    public void TheInstallerIsFlyleafsPackage()
    {
        // PC8: the Data Layer needs Flyleaf's package at both ends.
        var project = Text(Installer("Point.Installer.csproj"));
        Assert.Contains($"<ApplicationId>{WatchIdentity.InstallerPackage}</ApplicationId>", project, StringComparison.Ordinal);

        var manifest = Text(Installer("Properties", "AndroidManifest.xml"));
        Assert.Matches($"<manifest\\b[^>]*\\spackage=\"{Regex.Escape(WatchIdentity.InstallerPackage)}\"", manifest);
    }

    [Fact]
    public void TheFaceHasItsOwnPackage()
    {
        var manifest = Text(Path.Combine(WatchFiles.RepoRoot(), "Watch", "Point.Face", "AndroidManifest.xml"));

        Assert.Matches($"<manifest\\b[^>]*\\spackage=\"{Regex.Escape(WatchIdentity.FacePackage)}\"", manifest);
    }

    // ---- the Data Layer (plan §2.5) ----

    [Fact]
    public void PathsAreFrozen()
    {
        Assert.Equal("/update/apk", WatchIdentity.UpdateApkPath);
        Assert.Equal("/update/installed", WatchIdentity.UpdateInstalledPath);
    }

    [Fact]
    public void TheCapabilityIsFrozen()
    {
        Assert.Equal("point-installer", WatchIdentity.InstallerCapability);
    }

    [Fact]
    public void TheInstallerAdvertisesItsCapability()
    {
        var wear = Text(Installer("Resources", "values", "wear.xml"));

        Assert.Matches(@"<string-array\s+name=""android_wear_capabilities""", wear);
        Assert.Contains($"<item>{WatchIdentity.InstallerCapability}</item>", wear, StringComparison.Ordinal);
    }

    [Fact]
    public void TheListenerHearsBothPaths()
    {
        var listener = InstallerSources().Select(File.ReadAllText).SingleOrDefault(source => source.Contains("class UpdateListenerService", StringComparison.Ordinal));
        Assert.True(listener is not null, "The installer has no UpdateListenerService.");

        Assert.Contains("DataPathPrefix = WatchIdentity.UpdateApkPath", listener, StringComparison.Ordinal);
        Assert.Contains("DataPath = WatchIdentity.UpdateInstalledPath", listener, StringComparison.Ordinal);
    }

    // ---- the installer's registered names, and what it keeps ----

    [Fact]
    public void ComponentsAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.focusphone.launcher.point.UpdateListenerService", WatchIdentity.UpdateListenerName);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.point.InstallStatusReceiver", WatchIdentity.InstallStatusReceiverName);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.point.ReplacedReceiver", WatchIdentity.ReplacedReceiverName);
        Assert.Equal(ExpectedInstallerComponents, WatchIdentity.InstallerComponents);
    }

    [Theory]
    [InlineData("UpdateListenerName")]
    [InlineData("InstallStatusReceiverName")]
    [InlineData("ReplacedReceiverName")]
    public void EachComponentIsRegisteredByItsName(string constant)
    {
        var registered = InstallerSources().Count(path => File.ReadAllText(path).Contains($"Name = WatchIdentity.{constant}", StringComparison.Ordinal));

        Assert.Equal(1, registered);
    }

    [Fact]
    public void StoredNamesAreFrozen()
    {
        Assert.Equal("point-installer", WatchIdentity.InstallerPreferences);
        Assert.Equal("results", WatchIdentity.ResultsKey);
        Assert.Equal("point-install", WatchIdentity.InstallChannel);
    }

    [Fact]
    public void TheIdentityIsLiterals()
    {
        var identity = Text(Path.Combine(WatchFiles.RepoRoot(), "Watch", "Watch.Domain", "WatchIdentity.cs"));
        var code = Regex.Replace(identity, @"///.*|//.*", string.Empty);

        Assert.DoesNotContain("nameof(", code, StringComparison.Ordinal);
        Assert.All(Regex.Matches(code, @"public const string (\w+) = ([^;]+);").Select(match => match.Groups[2].Value.Trim()),
            value => Assert.Matches(@"^""[^""]*""$", value));
    }

    [Fact]
    public void TheIdsAreNotCopiedIntoTheInstaller()
    {
        Assert.NotEmpty(InstallerSources());
        var copied = InstallerSources()
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"""(com\.storyworthtelling\.[\w.]+|/update/[\w/]*)"""))
            .Select(path => Path.GetRelativePath(WatchFiles.RepoRoot(), path))
            .ToList();

        Assert.True(copied.Count == 0, "Use WatchIdentity rather than a copy:\n" + string.Join("\n", copied));
    }

    // ---- the form words (A4's, held on the watch's side too) ----

    [Fact]
    public void FormWordsAreFrozen()
    {
        Assert.Equal("form", ApkForms.Field);
        Assert.Equal("phone", ApkForms.PhoneWord);
        Assert.Equal("watch", ApkForms.WatchWord);
        Assert.Equal("android.hardware.type.watch", ApkForms.WatchFeature);
        Assert.Equal(".watch.apk", ApkForms.WatchAssetSuffix);
    }

    // ---- the installer's manifest ----

    [Fact]
    public void TheInstallerIsAWatchApp()
    {
        var manifest = Text(Installer("Properties", "AndroidManifest.xml"));

        Assert.Matches(@"<uses-feature\s+android:name=""android\.hardware\.type\.watch""", manifest);
        Assert.Matches(@"<meta-data\s+android:name=""com\.google\.android\.wearable\.standalone""\s+android:value=""false""", manifest);
    }

    [Fact]
    public void TheInstallerAsksForThreePermissions()
    {
        var manifest = Text(Installer("Properties", "AndroidManifest.xml"));
        var asked = Regex.Matches(manifest, @"<uses-permission\s+android:name=""([^""]+)""").Select(match => match.Groups[1].Value);
        var attributes = InstallerSources().SelectMany(path => Regex.Matches(File.ReadAllText(path), @"\[assembly:\s*UsesPermission").Cast<Match>()).ToList();

        Assert.Equal(ExpectedInstallerPermissions.Order(), asked.Order());
        Assert.Empty(attributes);
    }

    [Fact]
    public void TheInstallerSeesTheWatchApps()
    {
        // Android 11+ shows an app only the packages it names; the installer compares versions with what it can see.
        var manifest = Text(Installer("Properties", "AndroidManifest.xml"));
        var queried = Regex.Matches(manifest, @"<package\s+android:name=""([^""]+)""").Select(match => match.Groups[1].Value);

        Assert.Equal(WatchIdentity.Packages.Where(package => package != WatchIdentity.InstallerPackage).Order(), queried.Order());
    }

    [Fact]
    public void TheInstallerHasNoScreen()
    {
        // WD §11: Point has no screens besides the face; the installer shows only the system's own prompt.
        var sources = InstallerSources().Select(File.ReadAllText).ToList();
        var manifest = Text(Installer("Properties", "AndroidManifest.xml"));

        Assert.NotEmpty(sources);
        Assert.DoesNotContain(sources, source => source.Contains("[Activity", StringComparison.Ordinal));
        Assert.DoesNotContain("<activity", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("android.intent.category.LAUNCHER", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInstallerNeverBuzzes()
    {
        // WD §7: four patterns, each with one meaning; asking to install is not one of them.
        var sources = string.Join("\n", InstallerSources().Select(File.ReadAllText));

        Assert.Contains("EnableVibration(false)", sources, StringComparison.Ordinal);
        Assert.Contains("NotificationImportance.Low", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("Vibrator", sources, StringComparison.Ordinal);
    }

    [Fact]
    public void FrozenSetsAreNotEmpty()
    {
        Assert.NotEmpty(ExpectedPackages);
        Assert.NotEmpty(ExpectedInstallerComponents);
        Assert.NotEmpty(ExpectedInstallerPermissions);
        Assert.NotEmpty(WatchIdentity.Packages);
        Assert.NotEmpty(WatchIdentity.InstallerComponents);
    }

    // ---- Flyleaf's half (W-3.1.3): the phone end of the same contract ----

    private static string Flyleaf(params string[] parts) =>
        Path.Combine([WatchFiles.RepoRoot(), "FocusPhone", "Flyleaf.Android", .. parts]);

    private static IEnumerable<string> FlyleafSources() =>
        Directory.EnumerateFiles(Flyleaf(), "*.cs", SearchOption.AllDirectories).Where(path => !WatchFiles.IsBuildOutput(path));

    [Fact]
    public void ThePhonesNamesAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.focusphone.launcher.WatchReportListener", WatchIdentity.PhoneReportListenerName);
        Assert.Equal("watchUpdates", WatchIdentity.PhonePreferences);
        Assert.Equal("sent", WatchIdentity.SentKey);
        Assert.Equal("lastRun", WatchIdentity.LastRunKey);
    }

    [Fact]
    public void FlyleafRegistersItsListenerByName()
    {
        var listener = FlyleafSources().Select(File.ReadAllText)
            .Where(source => source.Contains($"Name = WatchIdentity.{nameof(WatchIdentity.PhoneReportListenerName)}", StringComparison.Ordinal))
            .ToList();

        var source = Assert.Single(listener);
        Assert.Contains(": WearableListenerService", source, StringComparison.Ordinal);
        Assert.Contains("MessageClient.ActionMessageReceived", source, StringComparison.Ordinal);
        Assert.Contains("DataPath = WatchIdentity.UpdateInstalledPath", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FlyleafAsksForTheInstallerByItsCapability()
    {
        var sources = string.Join("\n", FlyleafSources().Select(File.ReadAllText));

        Assert.Contains("WatchIdentity.InstallerCapability", sources, StringComparison.Ordinal);
        Assert.Contains("UpdatePaths.ApkPathFor", sources, StringComparison.Ordinal);
        Assert.Contains("UpdateApkFrame.Header", sources, StringComparison.Ordinal);
        Assert.Contains("InstalledCodec.Encode(new InstalledAsk())", sources, StringComparison.Ordinal);
    }

    [Fact]
    public void TheIdsAreNotCopiedIntoFlyleaf()
    {
        // Flyleaf names the watch's paths, capability and stores only through WatchIdentity, so the two ends cannot drift.
        string[] watchWords =
        [
            WatchIdentity.UpdateApkPath, WatchIdentity.UpdateInstalledPath, WatchIdentity.InstallerCapability,
            WatchIdentity.PhoneReportListenerName, WatchIdentity.PhonePreferences,
        ];
        Assert.All(watchWords, word => Assert.False(string.IsNullOrEmpty(word)));

        var copied = FlyleafSources()
            .Where(path => watchWords.Any(word => File.ReadAllText(path).Contains($"\"{word}", StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(WatchFiles.RepoRoot(), path))
            .ToList();

        Assert.True(copied.Count == 0, "Use WatchIdentity rather than a copy:\n" + string.Join("\n", copied));
    }

    [Fact]
    public void FlyleafReadsTheSameCodec()
    {
        // One frame and one message layout for both ends: Flyleaf references Watch.Domain rather than a copy of it.
        var project = Text(Flyleaf("Flyleaf.Android.csproj"));

        Assert.Contains(@"..\..\Watch\Watch.Domain\Watch.Domain.csproj", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Xamarin.GooglePlayServices.Wearable\" />", project, StringComparison.Ordinal);
        Assert.DoesNotContain(FlyleafSources(), path => File.ReadAllText(path).Contains("BinaryPrimitives", StringComparison.Ordinal));
    }

    [Fact]
    public void FlyleafsBuildWatchesTheWatchsContracts()
    {
        // build-flyleaf.yml publishes the head by folder name, so CiHygieneTests' closure (which follows .csproj paths
        // named in a workflow) cannot see that Flyleaf now builds Watch.Domain. A codec change must still rebuild Flyleaf,
        // or the phone sends a frame the installer no longer reads.
        var workflow = Text(Path.Combine(WatchFiles.RepoRoot(), ".github", "workflows", "build-flyleaf.yml"));

        Assert.Contains("- 'Watch/Watch.Domain/**'", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWatchWorkflowChecksEveryFrozenPackage()
    {
        // build-watch.yml's APP ID CHECK reads each APK's package back against its line of the table (P14 W-3.2.1). The
        // workflow types the packages out, so it is held to the frozen ids here, from the watch's side.
        var workflow = Text(Path.Combine(WatchFiles.RepoRoot(), ".github", "workflows", "build-watch.yml"));

        Assert.Matches($@"(?m)^\s+Watch/Point\.Installer/Point\.Installer\.csproj {Regex.Escape(WatchIdentity.InstallerPackage)}\s*$", workflow);
        Assert.Contains($"FACE_PACKAGE: {WatchIdentity.FacePackage}", workflow, StringComparison.Ordinal);
    }
}
