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
