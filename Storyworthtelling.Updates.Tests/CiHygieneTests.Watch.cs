using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// P14 W-3.2.1, the watch workflow (plan §2.3). A branch push runs the watch's tests and FaceStudio's and nothing else;
/// main and a manual run build every watch app and the face, sign them with the suite's one keystore (the Data Layer
/// delivers only between apps of one signature, so an installer signed any other way never hears Flyleaf), number them
/// in band 11 (PC4), refuse to let an APK leave unless its package, form, icon, font and dependency graph read back
/// right, and publish them as watch APKs.
/// </summary>
public partial class CiHygieneTests
{
    private const string WatchWorkflowPath = ".github/workflows/build-watch.yml";

    private const string BranchOnly = "if: github.event_name == 'push' && github.ref != 'refs/heads/main'";

    private const string WatchTests = "dotnet test Watch/Watch.Tests/Watch.Tests.csproj";

    private const string FaceStudioTests = "dotnet test Tools/FaceStudio.Tests/FaceStudio.Tests.csproj";

    private const string FaceApkFolder = "Watch/Point.Face/bin/Release/";

    [Fact]
    public void BranchPushesRunTheWatchTestsAndNothingElse()
    {
        var yaml = WatchWorkflow();
        Assert.Contains("workflow_dispatch:", yaml, StringComparison.Ordinal);
        Assert.Contains("branches: [main, 'claude/**']", yaml, StringComparison.Ordinal);
        Assert.Matches(@"cancel-in-progress: \$\{\{ github\.ref != 'refs/heads/main' \}\}", yaml);

        var test = Job(yaml, "test");
        Assert.Contains(BranchOnly, test, StringComparison.Ordinal);
        Assert.Equal([WatchTests, FaceStudioTests], RunLines(test));
        Assert.DoesNotContain("workload:", test, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.", test, StringComparison.Ordinal);

        Assert.Contains(MainOrByHand, Job(yaml, "build"), StringComparison.Ordinal);
    }

    [Fact]
    public void FaceStudiosTestsRunOnceOnABranchPush()
    {
        // Minutes are the budget: a watch change used to run FaceStudio's tests in test-tools.yml, and would now run them
        // in build-watch.yml as well.
        var running = Workflows()
            .Where(path => File.ReadAllText(path).Contains(FaceStudioTests, StringComparison.Ordinal)
                && File.ReadAllText(path).Contains("'claude/**'", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["build-watch.yml"], running);
    }

    [Fact]
    public void MainBuildsEveryWatchAppAndTheFace()
    {
        var build = Job(WatchWorkflow(), "build");
        Assert.Matches(@"uses: \./\.github/actions/dotnet\s+with:\s+workload: android", build);
        Assert.Contains("java-version: '21'", build, StringComparison.Ordinal);

        // The tests run inside the build, and there FaceStudio's APK test may not pass quietly for want of aapt2.
        Assert.Contains(WatchTests, build, StringComparison.Ordinal);
        Assert.Contains(FaceStudioTests, build, StringComparison.Ordinal);
        Assert.Matches(@"FACESTUDIO_APK_REQUIRED: '1'", build);

        // Every watch app is one line of the table, with the package it declares, and is published from its own folder.
        var table = WatchHeadsTable(build);
        var apps = WatchApps(RepoRoot());
        Assert.NotEmpty(apps);
        Assert.Equal(apps.OrderBy(app => app.Project, StringComparer.Ordinal), table.OrderBy(app => app.Project, StringComparer.Ordinal));
        Assert.Contains("dotnet publish \"$proj\" -f net10.0-android -c Release", build, StringComparison.Ordinal);

        var publish = Step(build, "Publish to rolling GitHub Release");
        var upload = Step(build, "Upload APKs");
        foreach (var app in apps)
        {
            var folder = Path.GetDirectoryName(app.Project)!.Replace('\\', '/') + "/bin/Release/net10.0-android/publish/*-Signed.apk";
            Assert.True(publish.Contains(folder, StringComparison.Ordinal), $"build-watch.yml builds {app.Project} but doesn't publish {folder}.");
            Assert.True(upload.Contains(folder, StringComparison.Ordinal), $"build-watch.yml doesn't keep {folder} as an artifact.");
        }

        // The face: FaceStudio packs it, and it goes up from its own bin folder under its own package.
        Assert.Contains("dotnet run --project Tools/FaceStudio -- --repo-root . --apk", build, StringComparison.Ordinal);
        Assert.Contains($"FACE_PACKAGE: {FacePackage()}", build, StringComparison.Ordinal);
        Assert.Contains($"FACE_APK: {FaceApkFolder}{FacePackage()}-Signed.apk", build, StringComparison.Ordinal);
        Assert.Contains(FaceApkFolder, publish, StringComparison.Ordinal);
        Assert.Contains(FaceApkFolder, upload, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryWatchApkIsSignedWithTheSuitesKeystore()
    {
        var build = Job(WatchWorkflow(), "build");
        Assert.Contains("secrets.KEYSTORE_BASE64", build, StringComparison.Ordinal);

        // The heads, as every phone head is signed.
        var heads = Step(build, "Build the signed watch apps");
        foreach (var property in new[] { "-p:AndroidKeyStore=true", "-p:AndroidSigningKeyStore=", "-p:AndroidSigningKeyAlias=", "-p:AndroidSigningKeyPass=", "-p:AndroidSigningStorePass=" })
        {
            Assert.Contains(property, heads, StringComparison.Ordinal);
        }

        // The face is no .NET project, so it is aligned and signed with the SDK's own tools, the passwords read from the
        // environment rather than the command line, and verified.
        var face = Step(build, "Build the signed face");
        var align = face.IndexOf("zipalign", StringComparison.Ordinal);
        var sign = face.IndexOf("apksigner\" sign", StringComparison.Ordinal);
        Assert.True(align >= 0 && sign > align, "The face must be aligned before it is signed: apksigner's signature covers the alignment.");
        Assert.Contains("--ks-pass env:KEYSTORE_PASSWORD", face, StringComparison.Ordinal);
        Assert.Contains("--key-pass env:KEY_PASSWORD", face, StringComparison.Ordinal);
        Assert.Contains("apksigner\" verify", face, StringComparison.Ordinal);
        Assert.DoesNotContain("--print-certs", face, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWatchIsBandEleven()
    {
        var build = Job(WatchWorkflow(), "build");
        Assert.Matches(new Regex($@"uses: \./\.github/actions/version-code\s+with:\s+band: {SuiteVersionBands.WatchBand}\b"), build);
        Assert.Equal(11, SuiteVersionBands.WatchBand);

        // Every APK takes this run's code: the heads through ApplicationVersion, the face through FaceStudio.
        Assert.Contains("-p:ApplicationVersion=$VERSION_CODE", build, StringComparison.Ordinal);
        Assert.Contains("--version-code \"$VERSION_CODE\"", build, StringComparison.Ordinal);

        // And the band table records each of them as a watch APK in the watch band.
        var watchBands = SuiteVersionBands.All.Where(band => band.Form == ApkForm.Watch).ToList();
        foreach (var package in WatchApps(RepoRoot()).Select(app => app.Package).Append(FacePackage()))
        {
            var band = watchBands.SingleOrDefault(entry => entry.Package == package);
            Assert.True(band is not null, $"{package} is built in build-watch.yml but has no watch entry in SuiteVersionBands.");
            Assert.Equal(SuiteVersionBands.WatchBand, band!.Band);
        }
    }

    [Fact]
    public void TheWatchChecksAreFatalAndComeBeforeAnyApkLeaves()
    {
        var build = Job(WatchWorkflow(), "build");
        var built = build.IndexOf("- name: Build the signed face", StringComparison.Ordinal);
        var leaves = new[] { "- name: Upload APKs", "- name: Publish to rolling GitHub Release" }
            .Select(marker => build.IndexOf(marker, StringComparison.Ordinal))
            .ToList();
        Assert.True(built >= 0 && leaves.All(index => index > built), "The face is built after the APKs leave, or not at all.");

        foreach (var name in new[] { "APP ID CHECK", "FORM CHECK", "ABI CHECK", "ICON CHECK", "FONT CHECK", "DEPENDENCY CHECK" })
        {
            var at = build.IndexOf($"- name: {name}", StringComparison.Ordinal);
            Assert.True(at > built && leaves.All(index => at < index), $"{name} must run after the build and before any APK leaves.");

            var step = Step(build, name);
            Assert.DoesNotContain("continue-on-error", step, StringComparison.Ordinal);
            if (name == "DEPENDENCY CHECK")
            {
                Assert.Contains("python3 .github/scripts/check-dependency-graph.py .", step, StringComparison.Ordinal);
                continue;
            }

            Assert.Contains("set -e", step, StringComparison.Ordinal);
            Assert.Contains("exit 1", step, StringComparison.Ordinal);
            Assert.Contains("watch-apks.txt", step, StringComparison.Ordinal);
        }

        // Each reads the APK back, not the project that made it.
        var appId = Step(build, "APP ID CHECK");
        Assert.Contains("dump badging", appId, StringComparison.Ordinal);
        Assert.Contains("package: name='$package'", appId, StringComparison.Ordinal);
        Assert.Contains("versionCode='$VERSION_CODE'", appId, StringComparison.Ordinal);

        var form = Step(build, "FORM CHECK");
        Assert.Contains("dump badging", form, StringComparison.Ordinal);
        Assert.Contains($"name='{ApkForms.WatchFeature}'", form, StringComparison.Ordinal);
        Assert.Contains("uses-feature(-not-required)?", form, StringComparison.Ordinal);

        // Every watch app carries 32-bit ARM code as well as 64-bit (P14 release A fix round): a watch whose system runs
        // 32-bit userspace refuses an APK without it. The face carries no code.
        var abi = Step(build, "ABI CHECK");
        Assert.Contains("lib/armeabi-v7a/", abi, StringComparison.Ordinal);
        Assert.Contains("lib/arm64-v8a/", abi, StringComparison.Ordinal);
        Assert.Contains("unzip -l", abi, StringComparison.Ordinal);

        // A watch app with a launcher entry must carry an icon; one without (the installer) carries none to check, and the
        // face is shown in the picker by its preview.
        var icon = Step(build, "ICON CHECK");
        Assert.Contains("launchable-activity", icon, StringComparison.Ordinal);
        Assert.Contains("application-icon", icon, StringComparison.Ordinal);
        Assert.Contains("watch_face_info.xml", icon, StringComparison.Ordinal);
        Assert.Contains("preview.png", icon, StringComparison.Ordinal);

        // The face carries the watch's own Plex Mono, byte for byte.
        var font = Step(build, "FONT CHECK");
        Assert.Contains("res/font/plexmono.ttf", font, StringComparison.Ordinal);
        Assert.Contains("Watch/Fonts/IBMPlexMono-Medium.ttf", font, StringComparison.Ordinal);
        Assert.Contains("sha256sum", font, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWatchPublishesAsWatchFromMainOnly()
    {
        var build = Job(WatchWorkflow(), "build");

        Assert.Matches(new Regex(@"if: github\.ref == 'refs/heads/main'\s+uses: \./\.github/actions/publish-rolling-release"), build);
        Assert.Matches(@"(?m)^\s+form: watch\s*$", Step(build, "Publish to rolling GitHub Release"));
        // To the watch's own release, which no phone reads (P14 release A fix round): never the phone's.
        Assert.Matches($@"(?m)^\s+release: {Regex.Escape(ReleaseChannels.Watch)}\s*$", Step(build, "Publish to rolling GitHub Release"));
        Assert.Contains("name: Watch-Android", build, StringComparison.Ordinal);
        Assert.Contains("./.github/actions/prune-old-artifacts", build, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWatchFilterCoversWhatTheWatchTestsRead()
    {
        // Watch.Tests reads every file under Watch/ by content (the leak guard, the installer's manifest) and FaceStudio's
        // tests read the committed face and the watch's fonts, so the filter takes the whole folder, less its documents.
        var yaml = WatchWorkflow();
        var filters = PathFilters(yaml);

        Assert.Contains("Watch", filters);
        Assert.Contains("Tools/FaceStudio", filters);
        Assert.Contains("Tools/FaceStudio.Tests", filters);
        Assert.Contains("- '!Watch/**/*.md'", yaml, StringComparison.Ordinal);
        Assert.Contains("- '!Watch/design/**'", yaml, StringComparison.Ordinal);
        Assert.Contains("- '.github/workflows/build-watch.yml'", yaml, StringComparison.Ordinal);

        // Watch.Tests also holds Flyleaf's half of the update channel by its source, and Flyleaf's workflow by its filter
        // (P14 release A fix round, finding 6): a Flyleaf-only change must run them too.
        Assert.Contains("FocusPhone/Flyleaf.Android", filters);
        Assert.Contains("- '.github/workflows/build-flyleaf.yml'", yaml, StringComparison.Ordinal);
    }

    private static string WatchWorkflow()
    {
        var path = RepoFile(WatchWorkflowPath);
        Assert.True(File.Exists(path), "build-watch.yml doesn't exist, so no watch APK is built, signed or published.");
        return File.ReadAllText(path);
    }

    /// <summary>One job's text, from its <c>  name:</c> line under <c>jobs:</c> to the next job.</summary>
    private static string Job(string yaml, string name)
    {
        var jobs = yaml[yaml.IndexOf("\njobs:", StringComparison.Ordinal)..];
        var match = Regex.Match(jobs, $@"(?m)^  {Regex.Escape(name)}:\s*$(.*?)(?=^  [\w-]+:\s*$|\z)", RegexOptions.Singleline);
        Assert.True(match.Success, $"build-watch.yml has no '{name}' job.");
        return match.Groups[1].Value;
    }

    /// <summary>The text of one named step, from its <c>- name:</c> line to the next step.</summary>
    private static string Step(string yaml, string name)
    {
        var match = Regex.Match(yaml, $@"- name: {Regex.Escape(name)}\n(.*?)(?=\n\s*- name:|\n\s*- uses:|\z)", RegexOptions.Singleline);
        Assert.True(match.Success, $"build-watch.yml has no '{name}' step.");
        return match.Groups[1].Value;
    }

    // Every single-line run: in a job.
    private static List<string> RunLines(string job) =>
        Regex.Matches(job, @"(?m)^\s+run: (?!\|)(.+)$").Select(match => match.Groups[1].Value.Trim()).ToList();

    // The build job's WATCH_HEADS table: one "project package" line per watch app.
    private static List<(string Project, string Package)> WatchHeadsTable(string build)
    {
        var match = Regex.Match(build, @"WATCH_HEADS: \|\n((?:[ \t]+\S+\.csproj[ \t]+\S+[ \t]*\n)+)");
        Assert.True(match.Success, "build-watch.yml has no WATCH_HEADS table.");
        return match.Groups[1].Value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(parts => (parts[0], parts[1]))
            .ToList();
    }

    // Every app project under Watch/ (one with an ApplicationId), with the package it declares.
    private static List<(string Project, string Package)> WatchApps(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "Watch"), "*.csproj", SearchOption.AllDirectories)
            .Select(project => (Project: project, Id: XDocument.Load(project).Descendants("ApplicationId").FirstOrDefault()?.Value))
            .Where(app => app.Id is not null)
            .Select(app => (Path.GetRelativePath(root, app.Project).Replace('\\', '/'), app.Id!))
            .ToList();

    // The face's package, from its generated manifest.
    private static string FacePackage() =>
        (string)XDocument.Load(RepoFile("Watch/Point.Face/AndroidManifest.xml")).Root!.Attribute("package")!;
}
