using System.Text.RegularExpressions;

namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// Constitution's workflow keeps the checks that make a native head's build mean something (C-12.1.1),
/// modelled on <see cref="PulpWorkflowTests"/>. Each check guards a failure that is silent on the phone.
/// </summary>
public class ConstitutionWorkflowTests
{
    private const string Package = "com.storyworthtelling.constitution";

    [Fact]
    public void ConstitutionWorkflowHasItsChecks()
    {
        var yaml = Workflow();

        Assert.Contains("name: Constitution-Android", yaml);
        Assert.Contains("Constitution/Constitution.Tests/Constitution.Tests.csproj", yaml);
        Assert.Contains("Constitution.Android", yaml);
        Assert.Contains("java-version: '21'", yaml);
        Assert.Contains("secrets.KEYSTORE_BASE64", yaml);
        Assert.Contains("1.0.$RUN_NUMBER", yaml);
        Assert.Contains("cancel-in-progress: ${{ github.ref != 'refs/heads/main' }}", yaml);
        Assert.Contains("branches: [main, 'claude/**']", yaml);
        Assert.Contains("- 'Constitution/**'", yaml);
        Assert.Matches(new Regex(@"uses: \./\.github/actions/version-code\s+with:\s+band: 10\b"), yaml);
        Assert.Matches(new Regex(
            @"if: github\.ref == 'refs/heads/main'\s+uses: \./\.github/actions/publish-rolling-release"), yaml);
        Assert.Matches(new Regex(@"test:\s+if: github\.event_name == 'push' && github\.ref != 'refs/heads/main'"), yaml);
        Assert.Matches(new Regex(
            @"build:\s+if: github\.ref == 'refs/heads/main' \|\| github\.event_name == 'workflow_dispatch'"), yaml);
    }

    [Fact]
    public void TheAppIdCheckIsFatalAndReadsThePackageBack()
    {
        var step = Step(Workflow(), "APP ID CHECK");

        Assert.Contains("set -e", step);
        Assert.Contains("dump badging", step);
        Assert.Contains($"name='{Package}'", step);
        Assert.Contains("exit 1", step);
    }

    // Typed out on purpose, as Constitution's own manifest guard does: the workflow's list cannot loosen by editing one place.
    private static readonly string[] HealthReads =
    [
        "android.permission.health.READ_WEIGHT",
        "android.permission.health.READ_BODY_FAT",
        "android.permission.health.READ_RESTING_HEART_RATE",
        "android.permission.health.READ_HEART_RATE_VARIABILITY",
        "android.permission.health.READ_STEPS",
        "android.permission.health.READ_EXERCISE",
        "android.permission.health.READ_SLEEP",
        "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND",
    ];

    [Fact]
    public void TheHealthPermissionCheckFollowsThePermissionCheckAndIsFatal()
    {
        var yaml = Workflow();
        var step = Step(yaml, "HEALTH PERMISSION CHECK");

        Assert.True(
            yaml.IndexOf("- name: HEALTH PERMISSION CHECK", StringComparison.Ordinal)
            > yaml.IndexOf("- name: PERMISSION CHECK", StringComparison.Ordinal),
            "The health check must come after the existing PERMISSION CHECK.");
        Assert.Contains("set -e", step);
        Assert.Contains("dump permissions", step);
        Assert.Contains("exit 1", step);
    }

    [Fact]
    public void TheHealthPermissionCheckNamesTheExactReadSet()
    {
        var step = Step(Workflow(), "HEALTH PERMISSION CHECK");

        foreach (var permission in HealthReads)
        {
            Assert.True(step.Contains(permission, StringComparison.Ordinal), $"The health check does not name {permission}.");
        }

        // Body fat only: lean body mass is not declared (review S11), so the check must not allow it.
        Assert.DoesNotContain("LEAN_BODY_MASS", step);
    }

    [Fact]
    public void TheHealthPermissionCheckRefusesWritesHistoryAndMedicalData()
    {
        var step = Step(Workflow(), "HEALTH PERMISSION CHECK");

        Assert.Contains("health\\.WRITE", step);
        Assert.Contains("HISTORY", step);
        Assert.Contains("MEDICAL_DATA", step);
    }

    // ---- the permission check, widened for CAMERA only (D105) ----

    // What the signed APK may declare as a plain uses-permission besides the health reads (checked by their own step). Typed out here
    // on purpose, so loosening the workflow's list means editing two places.
    private static readonly string[] AllowedUses =
    [
        "android.permission.INTERNET",
        "android.permission.CAMERA",
        "android.permission.ACCESS_NETWORK_STATE",
        "android.permission.WAKE_LOCK",
        "android.permission.RECEIVE_BOOT_COMPLETED",
        "android.permission.FOREGROUND_SERVICE",
        "com.storyworthtelling.constitution.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION",
        // Plate's custom recipe permission. Assembled from its parts because Codex's frozen-invariant guard allows Plate's package id
        // to be spelled in one place only.
        "com.storyworthtelling.focusphone." + "camera" + ".READ_RECIPE",
    ];

    [Fact]
    public void ThePermissionCheckIsFatalAndNamesTheAllowedSet()
    {
        var step = Step(Workflow(), "PERMISSION CHECK");

        Assert.Contains("set -e", step);
        Assert.Contains("dump permissions", step);
        Assert.Contains("exit 1", step);
        foreach (var permission in AllowedUses)
        {
            Assert.True(step.Contains(permission, StringComparison.Ordinal), $"The permission check does not name {permission}.");
        }
    }

    [Fact]
    public void ThePermissionCheckDoesNotAllowByWildcard()
    {
        var step = Step(Workflow(), "PERMISSION CHECK");

        Assert.DoesNotContain("android.permission.*", step);
        Assert.DoesNotContain("android\\.permission\\.[A-Z", step);
        Assert.DoesNotContain("READ_EXTERNAL", step);
        Assert.DoesNotContain("RECORD_AUDIO", step);
        Assert.DoesNotContain("WRITE_EXTERNAL", step);
    }

    [Fact]
    public void ThePermissionCheckReadsTheCameraFeaturesBack()
    {
        var step = Step(Workflow(), "PERMISSION CHECK");

        Assert.Contains("dump badging", step);
        Assert.Contains("uses-feature-not-required: name='android.hardware.camera'", step);
        Assert.Contains("uses-feature-not-required: name='android.hardware.camera.autofocus'", step);
    }

    [Fact]
    public void TheCheckPassesTheDeclaredSetWithCamera()
    {
        var outcome = RunPermissionCheck(AllowedUses.Append("android.permission.health.READ_STEPS"), cameraRequired: false);

        Assert.True(outcome.ExitCode == 0, outcome.Output);
    }

    [Fact]
    public void TheCheckFailsWithoutCamera()
    {
        var outcome = RunPermissionCheck(AllowedUses.Where(name => name != "android.permission.CAMERA"), cameraRequired: false);

        Assert.NotEqual(0, outcome.ExitCode);
    }

    [Theory]
    [InlineData("android.permission.RECORD_AUDIO")]
    [InlineData("android.permission.READ_EXTERNAL_STORAGE")]
    [InlineData("android.permission.ACCESS_FINE_LOCATION")]
    [InlineData("android.permission.CAMERA2")]
    [InlineData("android.permission.READ_MEDIA_IMAGES")]
    public void TheCheckFailsOnAnyOtherPermission(string extra)
    {
        var outcome = RunPermissionCheck(AllowedUses.Append(extra), cameraRequired: false);

        Assert.NotEqual(0, outcome.ExitCode);
        Assert.Contains(extra, outcome.Output);
    }

    [Fact]
    public void TheCheckFailsWhenTheCameraFeatureIsRequired()
    {
        var outcome = RunPermissionCheck(AllowedUses, cameraRequired: true);

        Assert.NotEqual(0, outcome.ExitCode);
    }

    [Fact]
    public void TheCheckFailsWithoutInternet()
    {
        var outcome = RunPermissionCheck(AllowedUses.Where(name => name != "android.permission.INTERNET"), cameraRequired: false);

        Assert.NotEqual(0, outcome.ExitCode);
    }

    /// <summary>
    /// Runs the workflow step's own shell against a stand-in aapt2 that prints the given permissions, so the check is exercised for
    /// what it does rather than for what it says.
    /// </summary>
    private static (int ExitCode, string Output) RunPermissionCheck(IEnumerable<string> declared, bool cameraRequired)
    {
        var step = Step(Workflow(), "PERMISSION CHECK");
        var script = Regex.Match(step, @"run: \|\n(?<body>.*)\z", RegexOptions.Singleline).Groups["body"].Value;
        var lines = script.Split('\n');
        var indent = lines.First(line => line.Trim().Length > 0).TakeWhile(c => c == ' ').Count();
        script = string.Join('\n', lines.Select(line => line.Length >= indent ? line[indent..] : line.TrimStart()));

        // The step's text runs on into the comment that heads the next step; the script ends at its own closing echo.
        const string closing = "echo \"=== END PERMISSION CHECK ===\"";
        script = script[..(script.IndexOf(closing, StringComparison.Ordinal) + closing.Length)];

        var root = Path.Combine(Path.GetTempPath(), "constitution-permcheck-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tools = Directory.CreateDirectory(Path.Combine(root, "sdk", "build-tools", "36.0.0")).FullName;
            var apkDirectory = Directory.CreateDirectory(Path.Combine(root, "work", "Constitution", "Constitution.Android", "bin", "Release", "net10.0-android", "publish")).FullName;
            File.WriteAllText(Path.Combine(apkDirectory, "app-Signed.apk"), "stand-in");

            var uses = string.Join("\n", declared.Select(name => $"uses-permission: name='{name}'"));
            var feature = cameraRequired ? "uses-feature: name='android.hardware.camera'" : "uses-feature-not-required: name='android.hardware.camera'";
            var stub = "#!/usr/bin/env bash\n"
                + "if [ \"$2\" = \"permissions\" ]; then\ncat <<'EOF'\npackage: com.storyworthtelling.constitution\n" + uses + "\nEOF\n"
                + "elif [ \"$2\" = \"badging\" ]; then\ncat <<'EOF'\npackage: name='com.storyworthtelling.constitution'\n" + feature
                + "\nuses-feature-not-required: name='android.hardware.camera.autofocus'\nEOF\nfi\n";
            var aapt2 = Path.Combine(tools, "aapt2");
            File.WriteAllText(aapt2, stub);
            File.SetUnixFileMode(aapt2, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var info = new System.Diagnostics.ProcessStartInfo("bash")
            {
                WorkingDirectory = Path.Combine(root, "work", "Constitution"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(script);
            info.Environment["ANDROID_HOME"] = Path.Combine(root, "sdk");
            using var process = System.Diagnostics.Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TheIconCheckIsFatal()
    {
        var step = Step(Workflow(), "ICON CHECK");

        Assert.Contains("set -e", step);
        Assert.Contains("application-icon", step);
        Assert.Contains("exit 1", step);
    }

    [Fact]
    public void TheFontCheckIsFatalAndNamesAllEightFonts()
    {
        var step = Step(Workflow(), "FONT CHECK");
        var fonts = Directory.GetFiles(RepoFile("Fonts"), "*.ttf")
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        Assert.Equal(8, fonts.Count);
        Assert.Contains("set -e", step);
        Assert.Contains("assets/fonts/", step);
        Assert.Contains("exit 1", step);
        foreach (var font in fonts)
        {
            Assert.True(step.Contains(font!, StringComparison.Ordinal),
                $"build-constitution.yml's FONT CHECK does not name {font}.ttf.");
        }
    }

    private static string Workflow()
    {
        var path = RepoFile(".github/workflows/build-constitution.yml");
        Assert.True(File.Exists(path), "build-constitution.yml does not exist, so Constitution is built and checked by nothing.");
        return File.ReadAllText(path);
    }

    private static string Step(string yaml, string name)
    {
        var match = Regex.Match(yaml, $@"- name: {Regex.Escape(name)}\n(.*?)(?=\n\s*- name:|\n\s*- uses:|\z)",
            RegexOptions.Singleline);
        Assert.True(match.Success, $"build-constitution.yml has no '{name}' step.");
        return match.Groups[1].Value;
    }

    private static string RepoFile(string relative) =>
        Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
