using Xunit;

namespace Storyboarder.Tests.Guard;

/// <summary>
/// The version the owner reads, and the one the update channel compares.
///
/// <para><b>versionCode</b> comes from the shared <c>version-code</c> composite action at band 1,
/// the same as Post and Plate. Inlining a version here — or going back to <c>run_number</c> —
/// is the failure that has already happened twice in this repository: a renamed workflow
/// restarts the counter, the next build looks older than what is installed, and the update
/// channel rejects it silently and forever.</para>
///
/// <para><b>The display version</b> is the number in Settings. Canonizer shipped 0.1.x; Post and
/// Plate graduated to 1.0.x on exactly this occasion, and Folio joins them.</para>
/// </summary>
public class DisplayVersionTests
{
    private static string Workflow()
    {
        var path = Path.Combine(FolioInvariantsTests.RepoRoot(), ".github", "workflows", "build-storyboarder.yml");
        Assert.True(File.Exists(path), $"The head's workflow is missing: {FolioInvariantsTests.Relative(path)}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void DisplayVersionIsOnePointZero()
    {
        var workflow = Workflow();

        Assert.Contains("-p:ApplicationDisplayVersion=1.0.", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:ApplicationDisplayVersion=0.1.", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionCodeStillComesFromTheSharedBandAction()
    {
        var workflow = Workflow();

        Assert.Contains("uses: ./.github/actions/version-code", workflow, StringComparison.Ordinal);
        Assert.Contains("band: 1", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:ApplicationVersion=${{ steps.version.outputs.code }}", workflow, StringComparison.Ordinal);
    }

    // The owner downloads the build by the artifact's name.
    [Fact]
    public void TheBuildArtifactIsNamedForTheApp()
    {
        Assert.Contains("name: Folio-Android", Workflow(), StringComparison.Ordinal);
    }
}
