using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// R-G23: the version the owner reads in Settings' foot ("Index 1.0.214", mockup 10, D41), read out of
/// the workflow the way Folio's <c>DisplayVersionTests</c> reads Folio's. The display version is the CI
/// run number under 1.0; the csproj's date-stamped default is only what a local build shows, and is not
/// touched. versionCode is decided in one place, the shared band action, never here.
/// </summary>
public class DisplayVersionTests
{
    private static string Workflow()
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), "..", ".github", "workflows", "build-index.yml");
        Assert.True(File.Exists(path), "The head's workflow is missing: .github/workflows/build-index.yml");
        return File.ReadAllText(path);
    }

    [Fact]
    public void DisplayVersionIsOnePointZeroDotRunNumber()
    {
        var workflow = Workflow();

        Assert.Contains("-p:ApplicationDisplayVersion=1.0.${{ github.run_number }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:ApplicationDisplayVersion=0.1.", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionCodeStillComesFromTheSharedBandAction()
    {
        var workflow = Workflow();

        Assert.Contains("uses: ./.github/actions/version-code", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:ApplicationVersion=${{ steps.version.outputs.code }}", workflow, StringComparison.Ordinal);
    }
}
