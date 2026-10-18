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
