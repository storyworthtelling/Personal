namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// Index's Add form (mockup 4, <c>.seg span{flex:1}</c>) needs the segmented control to fill its row. The
/// option is ADDITIVE and OFF by default, so Foreword and every other caller keep the content-width control
/// they were drawn with. Read by content: <c>PaperSegmented</c> is a MAUI view and cannot be constructed
/// in a plain xunit run.
/// </summary>
public class PaperSegmentedFillWidthTests
{
    [Fact]
    public void FillingTheWidthIsAnOptionThatDefaultsToOff()
    {
        var source = Source();

        Assert.Contains("bool fillWidth = false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefaultControlIsStillContentWidth()
    {
        var source = Source();

        // The original behaviour is kept verbatim on the non-fill path.
        Assert.Contains("GridLength.Auto", source, StringComparison.Ordinal);
        Assert.Contains("LayoutOptions.Start", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FillingGivesEverySegmentAnEqualShareOfTheRow()
    {
        var source = Source();

        Assert.Contains("GridLength.Star", source, StringComparison.Ordinal);
        Assert.Contains("LayoutOptions.Fill", source, StringComparison.Ordinal);
        Assert.Contains("fillWidth ?", source, StringComparison.Ordinal);
    }

    private static string Source() => File.ReadAllText(RepoFile("Storyworthtelling.Maui/Views/PaperSegmented.cs"));

    private static string RepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
