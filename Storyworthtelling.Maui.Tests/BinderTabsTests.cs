using System.Text.RegularExpressions;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// The MAUI binder's gutter fills with the active area's cloth, the same way
/// <c>Codex.Ui.Android</c>'s <c>BinderTabs.UpdateGutterColor</c> already does natively
/// (DESIGN.md §9, D121). Before this, <c>strip</c> — the <c>AbsoluteLayout</c> everything is added
/// to — never had a <c>BackgroundColor</c> set, so the gaps between and around the tabs fell
/// through to whatever the page behind this view was, instead of reading as the same cloth the
/// tabs around them wear.
///
/// <para><c>BinderTabs : ContentView</c> is a real MAUI view and cannot be meaningfully
/// constructed or measured in a plain xunit run — MAUI views need a running app host, which this
/// test project does not carry (see <c>NativeParityTests</c>/<c>DogEarPaletteTests</c> for the
/// same read-from-source approach used elsewhere in this project for exactly that reason). So this
/// is a source-level guard: it reads <c>Rebuild()</c>'s own text and asserts the fill is set inside
/// it, from the active area's cloth, with a sensible fallback when there are no areas yet.</para>
/// </summary>
public class BinderTabsTests
{
    [Fact]
    public void RebuildFillsTheGutterWithTheActiveAreasCloth()
    {
        var rebuild = RebuildBody();

        Assert.Matches(
            new Regex(@"strip\.BackgroundColor\s*=.*areas\[activeIndex\]\.Cloth", RegexOptions.Singleline),
            rebuild);
    }

    /// <summary>
    /// Guards the fallback too — with no areas yet, the gutter must still be filled rather than
    /// left transparent, so a phone that opens on an empty strip does not show a hole back to the
    /// page underneath.
    /// </summary>
    [Fact]
    public void RebuildFillsTheGutterEvenWithNoAreasYet()
    {
        var rebuild = RebuildBody();

        Assert.Matches(
            new Regex(@"strip\.BackgroundColor\s*=.*PaperResources\.Colour\(""Paper""\)", RegexOptions.Singleline),
            rebuild);
    }

    /// <summary>
    /// The fill must be set unconditionally at the top of <c>Rebuild()</c>, before the early
    /// return (<c>areas.Count == 0</c>) — otherwise the gutter stays unfilled until the first tab is
    /// actually laid out, and the same hole-back-to-the-page bug reappears for every frame before
    /// that. The strip is never bailed out of for want of a height: it paints at a placeholder
    /// height until one is known.
    /// </summary>
    [Fact]
    public void TheFillIsSetBeforeTheEarlyReturn()
    {
        var rebuild = RebuildBody();

        var fillIndex = rebuild.IndexOf("strip.BackgroundColor", StringComparison.Ordinal);
        var earlyReturnIndex = rebuild.IndexOf("areas.Count == 0", StringComparison.Ordinal);

        Assert.True(fillIndex >= 0, "Rebuild() never sets strip.BackgroundColor at all.");
        Assert.True(earlyReturnIndex >= 0, "Rebuild() no longer returns early for a strip with no areas.");
        Assert.True(fillIndex < earlyReturnIndex,
            "The gutter fill must be set before the early return, so it holds even before the "
            + "strip has a height to lay tabs out in.");
        Assert.DoesNotContain("Height <= 0", rebuild);
        Assert.Contains("BinderTabLayout.PreferredHeightDp * (areas.Count + 1)", rebuild);
    }

    private static string RebuildBody()
    {
        var source = File.ReadAllText(RepoFile("Storyworthtelling.Maui/Views/BinderTabs.cs"));

        var match = Regex.Match(
            source,
            @"private void Rebuild\(\)\s*\{(?<body>.*?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(match.Success, "Could not find Rebuild()'s body in BinderTabs.cs.");
        return match.Groups["body"].Value;
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
