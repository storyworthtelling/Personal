namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// P14 W-3.1.1: the publish action stages every APK through <c>.github/scripts/stage-apks.py</c> before it goes up.
/// The script reads each APK's badging with aapt2, decides its form from <c>android.hardware.type.watch</c>, refuses an
/// APK whose form is not the one the workflow said it was publishing, names a watch APK so it can never clobber its
/// phone app's asset (they share a package, so .NET names both <c>&lt;package&gt;-Signed.apk</c>), and writes the sidecar
/// the phone reads. These run it here, against a stand-in aapt2 and, where the SDK is present, the real one.
/// </summary>
public class ApkStagingTests
{
    private const string Script = ".github/scripts/stage-apks.py";
    private const string Post = "com.storyworthtelling.focusphone.communication";

    private static string Badging(string package, long versionCode, string? watchLine) => $"""
        package: name='{package}' versionCode='{versionCode}' versionName='1.0.7' platformBuildVersionName='16' platformBuildVersionCode='36'
        minSdkVersion:'34'
        targetSdkVersion:'36'
        application: label='Probe' icon=''
        feature-group: label=''
        {watchLine ?? "  uses-feature: name='android.hardware.faketouch'"}
          uses-implied-feature: name='android.hardware.faketouch' reason='default feature for all apps'
        supports-screens: 'small' 'normal' 'large' 'xlarge'

        """;

    private const string Required = "  uses-feature: name='android.hardware.type.watch'";
    private const string NotRequired = "  uses-feature-not-required: name='android.hardware.type.watch'";

    [Fact]
    public void APhoneApkKeepsItsName()
    {
        var bench = new Bench();
        var apk = bench.Apk("phone", $"{Post}-Signed.apk", Badging(Post, 103_500_000, null));

        var run = bench.Stage("phone", apk);

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Equal([$"{Post}-Signed.apk", $"{Post}-Signed.apk.json"], bench.Staged());
        Assert.Equal(new InstallCandidate(Post, 103_500_000, ApkForm.Phone), bench.Sidecar($"{Post}-Signed.apk"));
    }

    [Theory]
    [InlineData(Required)]
    [InlineData(NotRequired)]
    public void AWatchApkSaysWatchAndIsNamedForIt(string watchLine)
    {
        var bench = new Bench();
        var apk = bench.Apk("dash", $"{Post}-Signed.apk", Badging(Post, 1_103_500_000, watchLine));

        var run = bench.Stage("watch", apk);

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Equal([$"{Post}-Signed.watch.apk", $"{Post}-Signed.watch.apk.json"], bench.Staged());
        Assert.Equal(new InstallCandidate(Post, 1_103_500_000, ApkForm.Watch), bench.Sidecar($"{Post}-Signed.watch.apk"));
        Assert.Equal(ApkForm.Watch, ApkForms.FromAssetName($"{Post}-Signed.watch.apk"));
    }

    [Fact]
    public void AWatchApkInAPhonePublishStopsIt()
    {
        var bench = new Bench();
        var phone = bench.Apk("phone", "com.storyworthtelling.focusphone.audio-Signed.apk", Badging("com.storyworthtelling.focusphone.audio", 103_500_000, null));
        var watch = bench.Apk("wave", "com.storyworthtelling.focusphone.audio-Signed.apk", Badging("com.storyworthtelling.focusphone.audio", 1_103_500_000, Required));

        var run = bench.Stage("phone", phone, watch);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("watch", run.Output, StringComparison.Ordinal);
        Assert.Empty(bench.Staged());
    }

    [Fact]
    public void APhoneApkInAWatchPublishStopsIt()
    {
        // A watch app that forgot its uses-feature reads as a phone APK by every measure, and would replace its phone
        // app; the workflow said watch, so it stops here.
        var bench = new Bench();
        var forgot = bench.Apk("beat", "com.storyworthtelling.constitution-Signed.apk", Badging("com.storyworthtelling.constitution", 1_103_500_000, null));

        var run = bench.Stage("watch", forgot);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("android.hardware.type.watch", run.Output, StringComparison.Ordinal);
        Assert.Empty(bench.Staged());
    }

    [Fact]
    public void APhoneApkNamedLikeAWatchApkStopsIt()
    {
        var bench = new Bench();
        var apk = bench.Apk("phone", $"{Post}-Signed.watch.apk", Badging(Post, 103_500_000, null));

        Assert.NotEqual(0, bench.Stage("phone", apk).ExitCode);
    }

    [Fact]
    public void TwoApksOneNameStopsIt()
    {
        var bench = new Bench();
        var first = bench.Apk("a", "com.storyworthtelling.watch.point-Signed.apk", Badging("com.storyworthtelling.watch.point", 1_103_500_000, Required));
        var second = bench.Apk("b", "com.storyworthtelling.watch.point-Signed.watch.apk", Badging("com.storyworthtelling.watch.point", 1_103_500_001, Required));

        var run = bench.Stage("watch", first, second);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Empty(bench.Staged());
    }

    [Theory]
    [InlineData("tablet")]
    [InlineData("")]
    [InlineData("Watch")]
    public void AnUnknownFormStopsIt(string form)
    {
        var bench = new Bench();
        var apk = bench.Apk("phone", $"{Post}-Signed.apk", Badging(Post, 103_500_000, null));

        Assert.NotEqual(0, bench.Stage(form, apk).ExitCode);
        Assert.Empty(bench.Staged());
    }

    [Theory]
    [InlineData("minSdkVersion:'34'\n")]
    [InlineData("package: name='' versionCode='5'\n")]
    [InlineData("package: name='com.storyworthtelling.pulp' versionName='1.0'\n")]
    public void AnApkThatWontReadStopsIt(string badging)
    {
        var bench = new Bench();
        var apk = bench.Apk("odd", "odd-Signed.apk", badging);

        Assert.NotEqual(0, bench.Stage("phone", apk).ExitCode);
        Assert.Empty(bench.Staged());
    }

    [Fact]
    public void Aapt2FailingStopsIt()
    {
        var bench = new Bench();
        var apk = bench.Apk("gone", "gone-Signed.apk", badging: null);

        Assert.NotEqual(0, bench.Stage("phone", apk).ExitCode);
    }

    [Fact]
    public void TheRealAapt2AgreesWithThePhone()
    {
        // The same script over APKs linked by the SDK's own aapt2: its badging is the one CI reads. Skipped only on a
        // machine with no Android SDK.
        if (ProbeApks.Sdk() is not { } sdk)
        {
            return;
        }
        var bench = new Bench();
        var watchFolder = Directory.CreateDirectory(Path.Combine(bench.Root, "real-watch")).FullName;
        var phoneFolder = Directory.CreateDirectory(Path.Combine(bench.Root, "real-phone")).FullName;
        var watch = ProbeApks.Link(sdk, ProbeApks.WatchManifest, watchFolder, "com.example.probe-Signed.apk");
        var phone = ProbeApks.Link(sdk, ProbeApks.PhoneManifest, phoneFolder, "com.example.probe-Signed.apk");

        var watchRun = bench.StageWith(sdk.Aapt2, "watch", watch);
        var phoneRun = bench.StageWith(sdk.Aapt2, "phone", phone);

        Assert.True(watchRun.ExitCode == 0, watchRun.Output);
        Assert.True(phoneRun.ExitCode == 0, phoneRun.Output);
        Assert.Equal(new InstallCandidate("com.example.probe", 1_100_000_123, ApkForm.Watch), bench.Sidecar("com.example.probe-Signed.watch.apk"));
        Assert.Equal(new InstallCandidate("com.example.probe", 1_100_000_123, ApkForm.Phone), bench.Sidecar("com.example.probe-Signed.apk"));
        using var staged = File.OpenRead(Path.Combine(bench.Out, "com.example.probe-Signed.watch.apk"));
        Assert.Equal(ApkForm.Watch, ApkManifest.FormOf(staged));
        Assert.Equal(1, bench.StageWith(sdk.Aapt2, "phone", watch).ExitCode);
    }

    /// <summary>A scratch folder, a stand-in aapt2 that prints the badging kept beside each APK, and the script's output.</summary>
    private sealed class Bench
    {
        public Bench()
        {
            Root = Directory.CreateTempSubdirectory("stage-").FullName;
            Out = Path.Combine(Root, "out");
            Aapt2 = Path.Combine(Root, "aapt2");
            File.WriteAllText(Aapt2, "#!/bin/sh\n[ \"$1\" = dump ] && [ \"$2\" = badging ] && [ -f \"$3.badging\" ] && exec cat \"$3.badging\"\necho \"aapt2: unreadable $3\" >&2\nexit 1\n");
            File.SetUnixFileMode(Aapt2, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public string Root { get; }

        public string Out { get; }

        private string Aapt2 { get; }

        public string Apk(string folder, string name, string? badging)
        {
            var directory = Directory.CreateDirectory(Path.Combine(Root, folder)).FullName;
            var path = Path.Combine(directory, name);
            using (var zip = ProbeApks.ZipOf(("AndroidManifest.xml", [0x03, 0x00])))
            {
                File.WriteAllBytes(path, zip.ToArray());
            }
            if (badging is not null)
            {
                File.WriteAllText(path + ".badging", badging);
            }
            return path;
        }

        public (int ExitCode, string Output) Stage(string form, params string[] apks) => StageWith(Aapt2, form, apks);

        public (int ExitCode, string Output) StageWith(string aapt2, string form, params string[] apks) =>
            ProbeApks.Run("python3", [Path.Combine(RepoRoot(), Script), "--aapt2", aapt2, "--form", form, "--out", Out, .. apks]);

        public List<string> Staged() => Directory.Exists(Out)
            ? Directory.EnumerateFiles(Out).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList()!
            : [];

        public InstallCandidate? Sidecar(string apkName) =>
            ApkSidecar.Read(File.ReadAllText(Path.Combine(Out, ApkSidecar.NameFor(apkName))));
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
