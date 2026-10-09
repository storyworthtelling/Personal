using System.Text.RegularExpressions;

namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// Pulp's workflow keeps the checks that make a native head's build mean something (P12 guard G3).
///
/// <para>Each check guards a failure that is silent on the phone: a wrong package id installs a
/// second app beside the first, a missing icon ships the default one (two heads did, for months), and
/// a misfiled font crashes the head on launch. The FONT CHECK names all eight fonts because the older
/// workflows name six, omitting the two most visible — every screen header's Serif Black and the
/// owner's voice.</para>
/// </summary>
public class PulpWorkflowTests
{
    private const string Package = "com.storyworthtelling.pulp";

    [Fact]
    public void PulpHasItsOwnBandAndHasShippedNothingBefore()
    {
        var pulp = SuiteVersionBands.All.SingleOrDefault(a => a.Package == Package);

        Assert.NotNull(pulp);
        Assert.Equal(9, pulp!.Band);
        Assert.Equal(0, pulp.LastShippedBeforeUnification);
    }

    [Fact]
    public void TheWorkflowComputesItsCodeFromTheSharedActionAtBandNine()
    {
        var yaml = Workflow();

        Assert.Matches(new Regex(@"uses: \./\.github/actions/version-code\s+with:\s+band: 9\b"), yaml);
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

        Assert.Contains("application-icon", step);
        Assert.Contains("exit 1", step);
    }

    [Fact]
    public void TheFontCheckIsFatalAndNamesEveryShippedFont()
    {
        var step = Step(Workflow(), "FONT CHECK");
        var fonts = Directory.GetFiles(RepoFile("Fonts"), "*.ttf")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        // Not vacuous: the suite ships eight fonts, and a check over none of them proves nothing.
        Assert.Equal(8, fonts.Count);
        Assert.Contains("assets/fonts/", step);
        Assert.Contains("exit 1", step);
        foreach (var font in fonts)
        {
            Assert.True(step.Contains(font!, StringComparison.Ordinal),
                $"build-pulp.yml's FONT CHECK does not name {font}.ttf.");
        }
    }

    [Fact]
    public void TheArtifactIsPulpAndMainPublishesToTheRollingRelease()
    {
        var yaml = Workflow();

        Assert.Contains("name: Pulp-Android", yaml);
        Assert.Contains("./.github/actions/prune-old-artifacts", yaml);
        Assert.Matches(new Regex(
            @"if: github\.ref == 'refs/heads/main'\s+uses: \./\.github/actions/publish-rolling-release"), yaml);
    }

    private static string Workflow()
    {
        var path = RepoFile(".github/workflows/build-pulp.yml");
        Assert.True(File.Exists(path), "build-pulp.yml does not exist, so Pulp is built and checked by nothing.");
        return File.ReadAllText(path);
    }

    /// <summary>The text of one named step, from its <c>- name:</c> line to the next step.</summary>
    private static string Step(string yaml, string name)
    {
        var match = Regex.Match(yaml, $@"- name: {Regex.Escape(name)}\n(.*?)(?=\n\s*- name:|\n\s*- uses:|\z)",
            RegexOptions.Singleline);
        Assert.True(match.Success, $"build-pulp.yml has no '{name}' step.");
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
