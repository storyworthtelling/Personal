using Xunit;

namespace Folio.Tests.Guard;

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
        // Band 8, not 1. Band 1 was chosen against a recorded last-shipped of 169 and was wrong:
        // Canonizer's csproj computed yearDigit + MMddHHmm, so the phone was holding 608111608 and
        // every band-1 build was refused silently. See SuiteVersionBands.
        Assert.Contains("band: 8", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:ApplicationVersion=${{ steps.version.outputs.code }}", workflow, StringComparison.Ordinal);
    }

    /// <summary>
    /// The csproj must not compute its own versionCode.
    ///
    /// <para>This is the actual cause of the band-1 mistake, and it is worth a guard of its own
    /// because the mistake was invisible from where anyone looked. <c>SuiteVersionBands</c> recorded
    /// what the <b>workflow</b> injected — a bare <c>run_number</c>, so 169 — while the <b>csproj</b>
    /// had been computing <c>yearDigit + MMddHHmm</c> since August, quietly shipping nine-digit codes
    /// to the phone. Both files set the same property and only one of them was being read.</para>
    ///
    /// <para>So the rule is that exactly one place decides: CI, through the shared band action. A
    /// literal here is fine — it is what CI overrides — but anything computed is a second source of
    /// truth, and the one that wins is whichever happens to be applied last.</para>
    /// </summary>
    [Fact]
    public void TheCsprojDoesNotComputeItsOwnVersionCode()
    {
        var path = Path.Combine(
            FolioInvariantsTests.RepoRoot(), "Storyboarder", "Folio.Android", "Folio.Android.csproj");
        var csproj = File.ReadAllText(path);

        var match = System.Text.RegularExpressions.Regex.Match(
            csproj, @"<ApplicationVersion>(?<value>.*?)</ApplicationVersion>");

        Assert.True(match.Success, "The csproj no longer declares ApplicationVersion at all.");
        Assert.True(int.TryParse(match.Groups["value"].Value.Trim(), out _),
            $"ApplicationVersion is computed as '{match.Groups["value"].Value}'. The versionCode comes "
            + "from the shared band action; a csproj that computes one is a second source of truth, "
            + "and that is exactly how Canonizer shipped 608111608 while the band table read 169.");
    }

    // The owner downloads the build by the artifact's name.
    [Fact]
    public void TheBuildArtifactIsNamedForTheApp()
    {
        Assert.Contains("name: Folio-Android", Workflow(), StringComparison.Ordinal);
    }
}
