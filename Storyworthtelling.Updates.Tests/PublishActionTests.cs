using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// P14 release A fix round, finding 1. The publish action's own shell, run here as GitHub runs it (the action's
/// <c>run:</c> block, its inputs and expressions put in place), against a stand-in <c>gh</c> that records what it was
/// asked and a stand-in aapt2 that prints each APK's badging. What is held: phone APKs go to <c>apps-latest</c> and only
/// there; watch APKs go to <c>watch-latest</c> and only there; a publish that names the other form's release stops
/// before <c>gh</c> is asked anything; and the watch's release is a prerelease that is never the repository's latest,
/// because every Flyleaf and Score already on a phone reads <c>releases/latest</c>.
/// </summary>
public class PublishActionTests
{
    private const string ActionPath = ".github/actions/publish-rolling-release/action.yml";
    private const string Post = "com.storyworthtelling.focusphone.communication";
    private const string Face = "com.example.face";

    private const string WatchFeature = "  uses-feature: name='android.hardware.type.watch'";

    [Fact]
    public void PhoneApksGoToTheirOwnReleaseOnly()
    {
        var bench = new Bench();
        bench.ReleaseExists(ReleaseChannels.Phone);
        var apk = bench.Apk("post", $"{Post}-Signed.apk", Post, 103_500_000, watch: false);

        var run = bench.Publish("phone", release: null, apk);

        Assert.True(run.ExitCode == 0, run.Output);
        var upload = Assert.Single(bench.Asked(), line => line.StartsWith("release upload", StringComparison.Ordinal));
        Assert.StartsWith($"release upload {ReleaseChannels.Phone} ", upload, StringComparison.Ordinal);
        Assert.Contains($"{Post}-Signed.apk ", upload, StringComparison.Ordinal);
        Assert.Contains($"{Post}-Signed.apk.json", upload, StringComparison.Ordinal);
        Assert.DoesNotContain(ReleaseChannels.Watch, string.Join("\n", bench.Asked()), StringComparison.Ordinal);
    }

    [Fact]
    public void WatchApksGoToTheirOwnReleaseOnly()
    {
        var bench = new Bench();
        bench.ReleaseExists(ReleaseChannels.Phone);
        bench.ReleaseExists(ReleaseChannels.Watch);
        var dash = bench.Apk("dash", $"{Post}-Signed.apk", Post, 1_103_500_000, watch: true);
        var face = bench.Apk("face", $"{Face}-Signed.apk", Face, 1_103_500_000, watch: true);

        var run = bench.Publish("watch", ReleaseChannels.Watch, dash, face);

        Assert.True(run.ExitCode == 0, run.Output);
        var upload = Assert.Single(bench.Asked(), line => line.StartsWith("release upload", StringComparison.Ordinal));
        Assert.StartsWith($"release upload {ReleaseChannels.Watch} ", upload, StringComparison.Ordinal);
        Assert.Contains($"{Post}-Signed.watch.apk ", upload, StringComparison.Ordinal);
        Assert.Contains($"{Face}-Signed.watch.apk ", upload, StringComparison.Ordinal);
        Assert.DoesNotContain(bench.Asked(), line => Regex.IsMatch(line, $@"\b{Regex.Escape(ReleaseChannels.Phone)}\b"));
    }

    [Fact]
    public void AWatchPublishToThePhonesReleaseStopsBeforeAnything()
    {
        var bench = new Bench();
        bench.ReleaseExists(ReleaseChannels.Phone);
        var dash = bench.Apk("dash", $"{Post}-Signed.apk", Post, 1_103_500_000, watch: true);

        foreach (var release in new[] { ReleaseChannels.Phone, null })
        {
            var run = bench.Publish("watch", release, dash);

            Assert.NotEqual(0, run.ExitCode);
            Assert.Contains(ReleaseChannels.Watch, run.Output, StringComparison.Ordinal);
            Assert.Empty(bench.Asked());
        }
    }

    [Fact]
    public void APhonePublishToTheWatchsReleaseStopsBeforeAnything()
    {
        var bench = new Bench();
        bench.ReleaseExists(ReleaseChannels.Watch);
        var post = bench.Apk("post", $"{Post}-Signed.apk", Post, 103_500_000, watch: false);

        var run = bench.Publish("phone", ReleaseChannels.Watch, post);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains(ReleaseChannels.Phone, run.Output, StringComparison.Ordinal);
        Assert.Empty(bench.Asked());
    }

    [Theory]
    [InlineData("phone", "Apps-Latest")]
    [InlineData("phone", "nightly")]
    [InlineData("watch", "watch-latest ")]
    [InlineData("tablet", "apps-latest")]
    public void AnyOtherPairStops(string form, string release)
    {
        var bench = new Bench();
        bench.ReleaseExists(ReleaseChannels.Phone);
        bench.ReleaseExists(ReleaseChannels.Watch);
        var apk = bench.Apk("post", $"{Post}-Signed.apk", Post, 103_500_000, watch: false);

        var run = bench.Publish(form, release, apk);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Empty(bench.Asked());
    }

    [Fact]
    public void TheStepReadsItsInputsFromItsEnvironment()
    {
        // What the bench sets is what GitHub sets: the step's env: block maps each input this shell reads.
        var action = File.ReadAllText(RepoFile(ActionPath));

        Assert.Contains("FORM: ${{ inputs.form }}", action, StringComparison.Ordinal);
        Assert.Contains("RELEASE: ${{ inputs.release }}", action, StringComparison.Ordinal);
        Assert.Contains("GH_TOKEN: ${{ github.token }}", action, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReleaseDefaultsToThePhones()
    {
        var action = File.ReadAllText(RepoFile(ActionPath));

        Assert.Equal(ReleaseChannels.Phone, Bench.DefaultRelease(action));
    }

    [Fact]
    public void TheWatchsReleaseIsCreatedAsAPrereleaseThatIsNeverLatest()
    {
        var bench = new Bench();
        bench.ReleaseExists(ReleaseChannels.Phone);
        var dash = bench.Apk("dash", $"{Post}-Signed.apk", Post, 1_103_500_000, watch: true);

        var run = bench.Publish("watch", ReleaseChannels.Watch, dash);

        Assert.True(run.ExitCode == 0, run.Output);
        var create = Assert.Single(bench.Asked(), line => line.StartsWith("release create", StringComparison.Ordinal));
        Assert.StartsWith($"release create {ReleaseChannels.Watch} ", create, StringComparison.Ordinal);
        Assert.Contains(" --prerelease ", create, StringComparison.Ordinal);
        Assert.Contains(" --latest=false ", create, StringComparison.Ordinal);
        Assert.Contains($"{Post}-Signed.watch.apk", create, StringComparison.Ordinal);
        Assert.DoesNotContain(bench.Asked(), line => line.Contains("release upload", StringComparison.Ordinal));
    }

    [Fact]
    public void AnExistingWatchReleaseIsKeptOffLatestBeforeAnythingGoesUp()
    {
        // However the release came to be latest (a hand edit, a different create), it is put back to a prerelease before
        // a watch APK lands on it: a phone that reads releases/latest must never find one.
        var bench = new Bench();
        bench.ReleaseExists(ReleaseChannels.Watch);
        var dash = bench.Apk("dash", $"{Post}-Signed.apk", Post, 1_103_500_000, watch: true);

        var run = bench.Publish("watch", ReleaseChannels.Watch, dash);

        Assert.True(run.ExitCode == 0, run.Output);
        var asked = bench.Asked();
        var edit = asked.FindIndex(line => line.StartsWith($"release edit {ReleaseChannels.Watch} ", StringComparison.Ordinal));
        var upload = asked.FindIndex(line => line.StartsWith($"release upload {ReleaseChannels.Watch} ", StringComparison.Ordinal));
        Assert.True(edit >= 0 && upload > edit, string.Join("\n", asked));
        Assert.Contains(" --prerelease", asked[edit], StringComparison.Ordinal);
        Assert.Contains(" --latest=false", asked[edit], StringComparison.Ordinal);
    }

    [Fact]
    public void ThePhonesReleaseIsCreatedAsTheLatest()
    {
        var bench = new Bench();
        var post = bench.Apk("post", $"{Post}-Signed.apk", Post, 103_500_000, watch: false);

        var run = bench.Publish("phone", ReleaseChannels.Phone, post);

        Assert.True(run.ExitCode == 0, run.Output);
        var create = Assert.Single(bench.Asked(), line => line.StartsWith("release create", StringComparison.Ordinal));
        Assert.StartsWith($"release create {ReleaseChannels.Phone} ", create, StringComparison.Ordinal);
        Assert.Contains(" --latest ", create, StringComparison.Ordinal);
        Assert.DoesNotContain("--prerelease", create, StringComparison.Ordinal);
    }

    [Fact]
    public void AWatchApkInAPhonePublishStillStops()
    {
        // The form rule stays behind the release rule (defence in depth): a watch APK in a phone publish is refused by
        // the staging script whatever release was named.
        var bench = new Bench();
        bench.ReleaseExists(ReleaseChannels.Phone);
        var dash = bench.Apk("dash", $"{Post}-Signed.apk", Post, 1_103_500_000, watch: true);

        var run = bench.Publish("phone", ReleaseChannels.Phone, dash);

        Assert.NotEqual(0, run.ExitCode);
        Assert.DoesNotContain(bench.Asked(), line => line.StartsWith("release upload", StringComparison.Ordinal) || line.StartsWith("release create", StringComparison.Ordinal));
    }

    /// <summary>A scratch folder with a stand-in SDK (aapt2) and a stand-in gh, and the action's shell to run.</summary>
    private sealed class Bench
    {
        public Bench()
        {
            Root = Directory.CreateTempSubdirectory("publish-").FullName;
            AndroidHome = Path.Combine(Root, "sdk");
            var tools = Directory.CreateDirectory(Path.Combine(AndroidHome, "build-tools", "36.0.0")).FullName;
            Executable(Path.Combine(tools, "aapt2"),
                "#!/bin/sh\n[ \"$1\" = dump ] && [ \"$2\" = badging ] && [ -f \"$3.badging\" ] && exec cat \"$3.badging\"\necho \"aapt2: unreadable $3\" >&2\nexit 1\n");

            Bin = Directory.CreateDirectory(Path.Combine(Root, "bin")).FullName;
            Releases = Directory.CreateDirectory(Path.Combine(Root, "releases")).FullName;
            Log = Path.Combine(Root, "gh.log");
            // gh: every call is one line of the log; "release view <tag>" answers whether the release exists.
            Executable(Path.Combine(Bin, "gh"),
                "#!/bin/bash\necho \"$*\" >> \"$GH_LOG\"\nif [ \"$1\" = release ] && [ \"$2\" = view ]; then [ -f \"$GH_RELEASES/$3\" ]; exit $?; fi\nexit 0\n");
        }

        private string Root { get; }

        private string AndroidHome { get; }

        private string Bin { get; }

        private string Releases { get; }

        private string Log { get; }

        public void ReleaseExists(string tag) => File.WriteAllText(Path.Combine(Releases, tag), string.Empty);

        public string Apk(string folder, string name, string package, long versionCode, bool watch)
        {
            var directory = Directory.CreateDirectory(Path.Combine(Root, folder)).FullName;
            var path = Path.Combine(directory, name);
            using (var zip = ProbeApks.ZipOf(("AndroidManifest.xml", [0x03, 0x00])))
            {
                File.WriteAllBytes(path, zip.ToArray());
            }
            File.WriteAllText(path + ".badging", $"""
                package: name='{package}' versionCode='{versionCode}' versionName='1.0.7' platformBuildVersionName='16' platformBuildVersionCode='36'
                minSdkVersion:'34'
                feature-group: label=''
                {(watch ? WatchFeature : "  uses-feature: name='android.hardware.faketouch'")}

                """);
            return path;
        }

        /// <summary>The action's shell as GitHub runs it: inputs given (or the declared default), expressions put in place.</summary>
        public (int ExitCode, string Output) Publish(string form, string? release, params string[] apks)
        {
            if (File.Exists(Log))
            {
                File.Delete(Log);
            }

            var action = File.ReadAllText(RepoFile(ActionPath));
            var script = RunBlock(action)
                .Replace("${{ inputs.apks }}", string.Join(' ', apks), StringComparison.Ordinal)
                .Replace("${{ github.action_path }}", Path.GetDirectoryName(RepoFile(ActionPath))!, StringComparison.Ordinal);
            Assert.DoesNotContain("${{", script, StringComparison.Ordinal);

            var path = Path.Combine(Root, "step.sh");
            File.WriteAllText(path, script);
            var start = new ProcessStartInfo("bash", [path])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Root,
            };
            start.Environment["PATH"] = Bin + ":" + Environment.GetEnvironmentVariable("PATH");
            start.Environment["ANDROID_HOME"] = AndroidHome;
            start.Environment["GH_TOKEN"] = "stand-in";
            start.Environment["GH_LOG"] = Log;
            start.Environment["GH_RELEASES"] = Releases;
            start.Environment["FORM"] = form;
            start.Environment["RELEASE"] = release ?? DefaultRelease(action);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return (process.ExitCode, output.Result + error.Result);
        }

        public List<string> Asked() => File.Exists(Log) ? [.. File.ReadAllLines(Log)] : [];

        /// <summary>The <c>release</c> input's declared default, which GitHub gives a call that names none.</summary>
        public static string DefaultRelease(string action)
        {
            var match = Regex.Match(action, @"(?m)^  release:\s*\n(?:    .*\n)*?    default:\s*(\S+)\s*$");
            Assert.True(match.Success, "The publish action has no release input with a default.");
            return match.Groups[1].Value.Trim('\'', '"');
        }

        // The composite step's run: | block, unindented.
        private static string RunBlock(string action)
        {
            var match = Regex.Match(action, @"(?m)^(?<indent>[ ]+)run: \|\s*\n(?<body>(?:(?:\k<indent>[ ]+.*)?\n)+)");
            Assert.True(match.Success, "The publish action has no run block.");
            var indent = match.Groups["indent"].Value.Length + 2;
            return string.Join('\n', match.Groups["body"].Value.Split('\n').Select(line => line.Length >= indent ? line[indent..] : line.TrimStart()));
        }

        private static void Executable(string path, string text)
        {
            File.WriteAllText(path, text);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static string RepoFile(string relative)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CLAUDE.md")))
        {
            root = Path.GetDirectoryName(root);
        }
        return Path.Combine(root!, relative);
    }
}
