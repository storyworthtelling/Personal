using System.Text;

namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// P14 W-3.1.1 (plan §2.2, risk R1). A watch app carries its phone app's package, because the Data Layer delivers
/// only between apps with the same package and signature, and it is built in versionCode band 11, above every phone
/// band. Judged by package and version alone, Dash would be Post's newest build, Wave Score's, Beat Constitution's
/// and the Point installer Flyleaf's, and the phone would install each over the app it shares a name with, silently.
/// So every APK says what it is built for, the phone takes phone APKs only and the watch watch APKs only, the form is
/// judged before the version, and a form that can't be read is installed nowhere.
/// </summary>
public class WatchFormTests
{
    private const string Post = "com.storyworthtelling.focusphone.communication";
    private const string Score = "com.storyworthtelling.focusphone.audio";
    private const string Constitution = "com.storyworthtelling.constitution";
    private const string Flyleaf = "com.storyworthtelling.focusphone.launcher";

    // Band 1 (Post, Score, Flyleaf), band 10 (Constitution) and band 11 (every watch APK), as the version-code action
    // gives them in Nov 2026.
    private const long PhoneBuild = 103_500_000;
    private const long ConstitutionBuild = 1_003_500_000;
    private const long WatchBuild = 1_103_500_000;

    private static readonly IReadOnlyDictionary<string, long> PhoneHas = new Dictionary<string, long>
    {
        [Post] = PhoneBuild,
        [Score] = PhoneBuild,
        [Flyleaf] = PhoneBuild,
        // Constitution is not installed: the case where the old rule installs anything at all.
    };

    private static InstallCandidate[] WatchApps() =>
    [
        new(Post, WatchBuild, ApkForm.Watch),
        new(Score, WatchBuild, ApkForm.Watch),
        new(Constitution, WatchBuild, ApkForm.Watch),
        new(Flyleaf, WatchBuild, ApkForm.Watch),
    ];

    private static InstallCandidate[] PhoneApps() =>
    [
        new(Post, PhoneBuild, ApkForm.Phone),
        new(Score, PhoneBuild, ApkForm.Phone),
        new(Constitution, ConstitutionBuild, ApkForm.Phone),
        new(Flyleaf, PhoneBuild, ApkForm.Phone),
    ];

    [Fact]
    public void APhoneNeverInstallsAWatchApk()
    {
        Assert.Empty(UpdateScope.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Phone, WatchApps(), PhoneHas));
        Assert.Empty(UpdateScope.Decide(UpdateRole.SelfOnly, Score, ApkForm.Phone, WatchApps(), PhoneHas));
        Assert.Empty(UpdateScope.Decide(UpdateRole.SelfOnly, Constitution, ApkForm.Phone, WatchApps(), PhoneHas));
    }

    [Fact]
    public void TheWatchNeverInstallsAPhoneApk()
    {
        // The watch has none of them yet, which is when "not installed" would otherwise mean "install".
        var watchHas = new Dictionary<string, long>();

        Assert.Empty(UpdateScope.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Watch, PhoneApps(), watchHas));
        Assert.Empty(UpdateScope.Decide(UpdateRole.SelfOnly, Post, ApkForm.Watch, PhoneApps(), watchHas));
    }

    [Fact]
    public void EachFormInstallsItsOwn()
    {
        var all = PhoneApps().Concat(WatchApps()).ToList();

        var phone = UpdateScope.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Phone, all, new Dictionary<string, long>());
        var watch = UpdateScope.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Watch, all, new Dictionary<string, long>());

        Assert.Equal(PhoneApps(), phone);
        Assert.Equal(WatchApps(), watch);
    }

    [Fact]
    public void NoFormIsPhone()
    {
        // Every sidecar published before P14 has no form; every one of them is a phone APK.
        var candidate = ApkSidecar.Read("""{"package":"com.storyworthtelling.pulp","versionCode":900123}""");

        Assert.Equal(new InstallCandidate("com.storyworthtelling.pulp", 900123, ApkForm.Phone), candidate);
        Assert.Single(UpdateScope.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Phone, [candidate!], PhoneHas));
        Assert.Empty(UpdateScope.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Watch, [candidate!], PhoneHas));
    }

    [Theory]
    [InlineData("phone", ApkForm.Phone)]
    [InlineData("watch", ApkForm.Watch)]
    public void TheSidecarSaysItsForm(string word, ApkForm form)
    {
        var candidate = ApkSidecar.Read($$"""{"package":"{{Post}}","versionCode":{{WatchBuild}},"form":"{{word}}"}""");

        Assert.Equal(new InstallCandidate(Post, WatchBuild, form), candidate);
    }

    [Theory]
    [InlineData("\"tablet\"")]
    [InlineData("\"\"")]
    [InlineData("\"Watch\"")]
    [InlineData("\"PHONE\"")]
    [InlineData("\" phone\"")]
    [InlineData("null")]
    [InlineData("11")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[\"phone\"]")]
    public void AnUnknownFormIsInstalledNowhere(string value)
    {
        // A sidecar that names a form the channel doesn't know is not "no form": somebody wrote something, and it was
        // not "phone". Reading it as a phone APK would install a watch APK whose writer misspelt one word.
        var candidate = ApkSidecar.Read($$"""{"package":"{{Post}}","versionCode":{{WatchBuild}},"form":{{value}}}""");

        Assert.Equal(ApkForm.Unknown, candidate!.Form);
        Assert.Empty(UpdateScope.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Phone, [candidate], PhoneHas));
        Assert.Empty(UpdateScope.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Watch, [candidate], new Dictionary<string, long>()));
    }

    [Fact]
    public void ADeviceOfNoKnownFormInstallsNothing() =>
        Assert.Empty(UpdateScope.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Unknown,
            PhoneApps().Concat(WatchApps()).ToList(), new Dictionary<string, long>()));

    [Fact]
    public void SamePackageTwoForms()
    {
        // Post's phone APK and Dash, one release, one package, Post not yet on this phone. The phone takes the phone
        // APK and only the phone APK; a plan keyed by package would carry Dash along with it.
        var phoneApk = new ReleaseApk(41, new InstallCandidate(Post, PhoneBuild, ApkForm.Phone));
        var watchApk = new ReleaseApk(42, new InstallCandidate(Post, WatchBuild, ApkForm.Watch));
        var nothing = new Dictionary<string, long>();

        foreach (var order in new[] { new[] { phoneApk, watchApk }, [watchApk, phoneApk] })
        {
            var onPhone = SiblingUpdatePlan.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Phone, order, nothing)
                .ToDictionary(decision => decision.Apk.AssetId);
            var onWatch = SiblingUpdatePlan.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Watch, order, nothing)
                .ToDictionary(decision => decision.Apk.AssetId);

            Assert.True(onPhone[41].Install);
            Assert.Equal(InstallOutcome.NotForThisDevice, onPhone[42].Settled);
            Assert.True(onWatch[42].Install);
            Assert.Equal(InstallOutcome.NotForThisDevice, onWatch[41].Settled);
        }
    }

    [Fact]
    public void TheFormComesBeforeTheVersion()
    {
        // The phone already has this build of Post; Dash's band-11 code is "newer". On the phone the phone APK is
        // current and Dash is not for it: nothing installs, and Dash is never called Post's update.
        var phoneApk = new ReleaseApk(41, new InstallCandidate(Post, PhoneBuild, ApkForm.Phone));
        var watchApk = new ReleaseApk(42, new InstallCandidate(Post, WatchBuild, ApkForm.Watch));
        var beat = new ReleaseApk(43, new InstallCandidate(Constitution, WatchBuild, ApkForm.Watch));

        var decisions = SiblingUpdatePlan.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Phone,
                [phoneApk, watchApk, beat], new Dictionary<string, long> { [Post] = PhoneBuild, [Constitution] = ConstitutionBuild })
            .ToDictionary(decision => decision.Apk.AssetId, decision => decision.Settled);

        Assert.Equal(InstallOutcome.AlreadyCurrent, decisions[41]);
        Assert.Equal(InstallOutcome.NotForThisDevice, decisions[42]);
        Assert.Equal(InstallOutcome.NotForThisDevice, decisions[43]);
    }

    [Fact]
    public void ThePlanKeepsTheOldRulesWithinAForm()
    {
        var newer = new ReleaseApk(1, new InstallCandidate(Score, PhoneBuild + 1, ApkForm.Phone));
        var stray = new ReleaseApk(2, new InstallCandidate("com.example.other", 9, ApkForm.Phone));
        var older = new ReleaseApk(3, new InstallCandidate(Flyleaf, PhoneBuild - 1, ApkForm.Phone));

        var decisions = SiblingUpdatePlan.Decide(UpdateRole.Custodian, Flyleaf, ApkForm.Phone, [newer, stray, older], PhoneHas)
            .ToDictionary(decision => decision.Apk.AssetId, decision => decision.Settled);

        Assert.Null(decisions[1]);
        Assert.Equal(InstallOutcome.AlreadyCurrent, decisions[2]);
        Assert.Equal(InstallOutcome.AlreadyCurrent, decisions[3]);
    }

    [Fact]
    public void ReadingsMustAgree()
    {
        Assert.Equal(ApkForm.Phone, ApkForms.Agree(ApkForm.Phone, ApkForm.Phone, ApkForm.Phone));
        Assert.Equal(ApkForm.Watch, ApkForms.Agree(ApkForm.Watch, ApkForm.Watch));
        Assert.Equal(ApkForm.Unknown, ApkForms.Agree(ApkForm.Phone, ApkForm.Watch));
        Assert.Equal(ApkForm.Unknown, ApkForms.Agree(ApkForm.Watch, ApkForm.Phone, ApkForm.Watch));
        Assert.Equal(ApkForm.Unknown, ApkForms.Agree(ApkForm.Phone, ApkForm.Unknown));
        Assert.Equal(ApkForm.Unknown, ApkForms.Agree());
    }

    [Theory]
    [InlineData("com.storyworthtelling.focusphone.communication-Signed.apk", ApkForm.Phone)]
    [InlineData("Flyleaf-Signed.apk", ApkForm.Phone)]
    [InlineData("com.storyworthtelling.focusphone.communication-Signed.watch.apk", ApkForm.Watch)]
    [InlineData("com.storyworthtelling.watch.point.WATCH.APK", ApkForm.Watch)]
    [InlineData("watch-Signed.apk", ApkForm.Phone)]
    public void AWatchAssetIsNamedForIt(string name, ApkForm form) => Assert.Equal(form, ApkForms.FromAssetName(name));

    [Fact]
    public void TheWatchFeatureMakesAWatch()
    {
        Assert.Equal(ApkForm.Watch, ApkForms.FromFeatures(["android.hardware.faketouch", "android.hardware.type.watch"]));
        Assert.Equal(ApkForm.Phone, ApkForms.FromFeatures(["android.hardware.faketouch", "android.hardware.type.television"]));
        Assert.Equal(ApkForm.Phone, ApkForms.FromFeatures([null]));
        Assert.Equal(ApkForm.Phone, ApkForms.FromFeatures(null));
    }

    [Fact]
    public void TheWordsAreFrozen()
    {
        Assert.Equal("form", ApkForms.Field);
        Assert.Equal("phone", ApkForms.PhoneWord);
        Assert.Equal("watch", ApkForms.WatchWord);
        Assert.Equal("android.hardware.type.watch", ApkForms.WatchFeature);
        Assert.Equal(".watch.apk", ApkForms.WatchAssetSuffix);
        Assert.Equal("phone", ApkForms.Word(ApkForm.Phone));
        Assert.Equal("watch", ApkForms.Word(ApkForm.Watch));

        // Literals, never nameof: a renamed member must not rename a word already on the release.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Storyworthtelling.Updates", "ApkForm.cs"));
        foreach (var literal in new[] { "\"form\"", "\"phone\"", "\"watch\"", "\"android.hardware.type.watch\"", "\".watch.apk\"" })
        {
            Assert.Contains(literal, source, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("nameof(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NotForThisDeviceIsSettled()
    {
        // Settled, so the six-hourly run does not fetch a watch APK again; and not a failure, so the phone's quiet
        // line does not report one.
        Assert.True(InstallOutcomes.ShouldMarkProcessed(InstallOutcome.NotForThisDevice));

        var run = UpdateRunResult.Ran([new AppInstallResult(Post, WatchBuild, InstallOutcome.NotForThisDevice, "form watch")]);

        Assert.Equal("Everything is up to date.", run.Summarize());
        Assert.False(run.AnyFailed);
        Assert.Equal([$"UPDATE {Post} {WatchBuild}: NotForThisDevice — form watch"], run.Lines());
    }

    // ---- The APK's own manifest: the reading taken from the bytes that would be installed ----

    [Fact]
    public void AManifestNamingTheWatchFeatureIsAWatch()
    {
        // aapt2 writes a compiled manifest's strings as UTF-16; UTF-8 pools exist too.
        using var utf16 = ProbeApks.Zip(Encoding.Unicode, "manifest", "uses-feature", "android.hardware.type.watch");
        using var utf8 = ProbeApks.Zip(Encoding.UTF8, "manifest", "android.hardware.type.watch", "application");

        Assert.Equal(ApkForm.Watch, ApkManifest.FormOf(utf16));
        Assert.Equal(ApkForm.Watch, ApkManifest.FormOf(utf8));
    }

    [Fact]
    public void AManifestWithoutItIsAPhone()
    {
        using var apk = ProbeApks.Zip(Encoding.Unicode, "manifest", "uses-feature", "android.hardware.faketouch");

        Assert.Equal(ApkForm.Phone, ApkManifest.FormOf(apk));
    }

    [Fact]
    public void WhatIsNotAnApkHasNoForm()
    {
        using var notAZip = new MemoryStream(Encoding.UTF8.GetBytes("android.hardware.type.watch is not a zip"));
        using var noManifest = ProbeApks.ZipOf(("classes.dex", [0x64, 0x65, 0x78]));

        Assert.Equal(ApkForm.Unknown, ApkManifest.FormOf(notAZip));
        Assert.Equal(ApkForm.Unknown, ApkManifest.FormOf(noManifest));
    }

    [Fact]
    public void ReadingLeavesTheStreamForTheInstaller()
    {
        using var apk = ProbeApks.Zip(Encoding.Unicode, "android.hardware.faketouch");

        ApkManifest.FormOf(apk);

        Assert.True(apk.CanRead);
        Assert.Equal(0, apk.Position);
    }

    [Fact]
    public void RealApksSayTheirForm()
    {
        // Linked by aapt2, as every APK in the suite is. Skipped only on a machine with no Android SDK.
        if (ProbeApks.Sdk() is not { } sdk)
        {
            return;
        }
        var folder = Directory.CreateTempSubdirectory("form-").FullName;

        foreach (var (manifest, form) in new[]
                 {
                     (ProbeApks.WatchManifest, ApkForm.Watch),
                     (ProbeApks.OptionalWatchManifest, ApkForm.Watch),
                     (ProbeApks.PhoneManifest, ApkForm.Phone),
                 })
        {
            var apk = ProbeApks.Link(sdk, manifest, folder, $"probe-{form}-{manifest.Length}.apk");
            using var stream = File.OpenRead(apk);
            Assert.Equal(form, ApkManifest.FormOf(stream));
        }
    }

    private static string RepoRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CLAUDE.md")))
        {
            root = Path.GetDirectoryName(root);
        }
        return root!;
    }
}
