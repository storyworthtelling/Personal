using System.Text.RegularExpressions;
using Codex.Tests.Design;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// R6 / remediation 3.5: the native dog-ear stays the same component as the MAUI kit's.
///
/// <para>The kit's <c>DogEar</c> gained two hues (Reference, Progress), an optional notice timestamp,
/// newest-first ordering and mockup 17's 40dp ear. The native one is Post's <c>FoldView</c> and is
/// separately drawn, so each of those is asserted here by reading <c>DogEar.cs</c> and
/// <c>WaitingActivity.cs</c> — <c>Codex.Ui.Android</c> is <c>net10.0-android</c> and cannot be
/// loaded in this plain run.</para>
/// </summary>
public class DogEarNativeTests
{
    /// <summary>
    /// Every hue the kit can carry has a native twin of the SAME NAME, so a caller moving between a
    /// MAUI app and a native head asks for "Progress" and gets the same colour.
    /// </summary>
    [Fact]
    public void EveryKitHueHasANativeHueOfTheSameName()
    {
        var kit = Regex.Match(
            File.ReadAllText(RepoFile("Storyworthtelling.Maui/State/DogEarState.cs")),
            @"enum DogEarHue\s*\{(?<body>.*?)\}", RegexOptions.Singleline).Groups["body"].Value;
        var names = Regex.Matches(kit, @"^\s*(?<n>[A-Z]\w+),", RegexOptions.Multiline).Select(m => m.Groups["n"].Value).ToList();
        Assert.NotEmpty(names);

        var native = Hues();
        foreach (var name in names)
        {
            Assert.True(
                Regex.IsMatch(native, $@"public static Color {name}\s*=>"),
                $"The kit's DogEarHue.{name} has no DogEar.Hues.{name} natively.");
        }
    }

    [Theory]
    [InlineData("Reference", "ReferenceMark")]
    [InlineData("Progress", "ProgressMark")]
    [InlineData("Problem", "ProblemMark")]
    [InlineData("Caution", "CautionMark")]
    [InlineData("Resolved", "ResolvedMark")]
    public void TheNativeHueIsTheRolesMarkFromPaperTheme(string hue, string theme)
    {
        Assert.Matches(new Regex($@"public static Color {hue}\s*=>\s*PaperTheme\.{theme};"), Hues());
    }

    [Fact]
    public void ActionIsThePaperThemeAccent_WhichIsTheActionMark()
    {
        Assert.Matches(new Regex(@"public static Color Action\s*=>\s*PaperTheme\.Accent;"), Hues());
    }

    /// <summary>Finding 6: 40dp, not 72, in both the undo and the notice fold.</summary>
    [Fact]
    public void TheEarIsFortyDp_NotSeventyTwo()
    {
        var source = DogEarSource();

        Assert.DoesNotContain("PaperTheme.Dp(context, 72)", source, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(source, @"PaperTheme\.Dp\(context, DogEarGeometry\.SizeDp\)").Count);
    }

    /// <summary>Finding 6: the arrow is a stroked path from the shared geometry, not the glyph.</summary>
    [Fact]
    public void TheBackTurnIsAStrokedPathNotAGlyphInAFont()
    {
        var source = DogEarSource();

        Assert.DoesNotContain("↶", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SerifSemibold", source, StringComparison.Ordinal);
        Assert.Contains("DogEarGeometry.ArrowArc", source, StringComparison.Ordinal);
        Assert.Contains("DogEarGeometry.ArrowHead", source, StringComparison.Ordinal);
        Assert.Contains("DogEarGeometry.FlapTip", source, StringComparison.Ordinal);
    }

    /// <summary>Finding 1: a native notice may carry the moment its cause began, defaulting to none.</summary>
    [Fact]
    public void ANoticeMayCarryATimestampThatDefaultsToNull()
    {
        Assert.Matches(
            new Regex(@"record Notice\([^)]*DateTimeOffset\? At = null\)", RegexOptions.Singleline),
            DogEarSource());
    }

    /// <summary>Finding 2: the box orders through the shared rule, not a bare Insert(0, ...).</summary>
    [Fact]
    public void NoticesAreOrderedByTheSharedRuleNotInsertedAtTheFront()
    {
        var source = DogEarSource();

        Assert.DoesNotContain("Entries.Insert(0", source, StringComparison.Ordinal);
        Assert.Contains("NoticeOrder", source, StringComparison.Ordinal);
    }

    /// <summary>Finding 1: the native page prints each notice's date too.</summary>
    [Fact]
    public void TheWaitingActivityPrintsEachNoticesDate()
    {
        var source = File.ReadAllText(RepoFile("FocusPhone/Codex.Ui.Android/WaitingActivity.cs"));

        Assert.Contains("RelativeTime.Format", source, StringComparison.Ordinal);
        Assert.Contains("notice.At", source, StringComparison.Ordinal);
        Assert.DoesNotContain("minus the relative date", source, StringComparison.Ordinal);
    }

    private static string Hues()
    {
        var match = Regex.Match(
            DogEarSource(), @"public static class Hues\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(match.Success, "DogEar has no nested Hues class.");
        return match.Groups["body"].Value;
    }

    private static string DogEarSource() => File.ReadAllText(RepoFile("FocusPhone/Codex.Ui.Android/DogEar.cs"));

    private static string RepoFile(string relative) =>
        Path.Combine(Directory.GetParent(DesignTestHelpers.FocusPhoneRoot())!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
}
