using System.Text.RegularExpressions;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// The printed ladder is the screen ladder at the registered ratio, and nothing else.
///
/// <para>The expected values are <b>read from the native tokens' own source file</b> rather than
/// restated here. A test holding its own copy of the sp ladder would pass while the two ladders
/// drifted apart — which is precisely the failure this release exists to close, and the one that
/// let the MAUI heads keep a retired accent for months.</para>
/// </summary>
public class PaperTypeScaleTests
{
    [Theory]
    [InlineData("Display", PaperTypeScale.Display)]
    [InlineData("Title", PaperTypeScale.Title)]
    [InlineData("Heading", PaperTypeScale.Heading)]
    [InlineData("Body", PaperTypeScale.Body)]
    [InlineData("Secondary", PaperTypeScale.Secondary)]
    [InlineData("Label", PaperTypeScale.Label)]
    [InlineData("Mono", PaperTypeScale.Mono)]
    public void EveryPrintedSizeIsItsScreenSizeAtTheRatio(string role, float points)
    {
        var sp = ScreenLadder()[role];

        Assert.Equal(sp * PaperTypeScale.Ratio, points, 3);
    }

    /// <summary>
    /// The ratio itself, pinned. It is the one number in the printed scale, so a change to it is a
    /// change to every sheet at once and should have to be written down deliberately.
    /// </summary>
    [Fact]
    public void TheRatioIsThreeQuarters()
    {
        Assert.Equal(0.75f, PaperTypeScale.Ratio);
    }

    /// <summary>
    /// The body role lands on 12 points — the size the sheets had already reached by eye, before
    /// any of this was derived. It is the evidence that the ratio was fitted to the work rather
    /// than chosen to make the arithmetic tidy.
    /// </summary>
    [Fact]
    public void TheBodyRoleLandsWhereTheSheetsAlreadyWere()
    {
        Assert.Equal(12f, PaperTypeScale.Body);
    }

    private static Dictionary<string, float> ScreenLadder()
    {
        var native = File.ReadAllText(RepoFile("FocusPhone/Codex.Domain/Design/DesignTokens.cs"));
        var block = Regex.Match(native, @"public static class TypeSp\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);

        Assert.True(block.Success, "Could not find TypeSp in the native design tokens.");

        return Regex.Matches(block.Groups["body"].Value, @"public const float (?<name>\w+) = (?<value>[\d.]+)f;")
            .ToDictionary(m => m.Groups["name"].Value, m => float.Parse(m.Groups["value"].Value));
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
