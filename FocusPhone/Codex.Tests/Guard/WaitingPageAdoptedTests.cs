using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Turning a notice fold now turns the page to a Waiting screen instead of opening the suite's
/// action-card popup (DESIGN.md §13, D162). Before this, <c>DogEar.OpenNotices</c> built
/// <c>CardAction</c> rows and called <c>ActionCard.Show</c> — a centred card over the page, the
/// exact shape mockup 17 replaces with a page turn.
///
/// <para><c>Codex.Ui.Android</c> is a <c>net10.0-android</c> project and its real types cannot be
/// instantiated in this plain xunit run, which has no Android runtime — the same reason
/// <c>Storyworthtelling.Maui</c>'s own view tests (<c>BinderTabsTests</c>, <c>DogEarPaletteTests</c>)
/// read their target's source rather than constructing it. So this is a source-level guard: it
/// reads <c>OpenNotices</c>'s own body and asserts it no longer reaches for <c>ActionCard.Show</c>
/// and does reach for the new page instead.</para>
/// </summary>
public class WaitingPageAdoptedTests
{
    [Fact]
    public void OpenNoticesNoLongerCallsActionCard()
    {
        var body = OpenNoticesBody();

        Assert.DoesNotContain("ActionCard.Show", body, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenNoticesLaunchesTheWaitingPage()
    {
        var body = OpenNoticesBody();

        Assert.Contains("WaitingActivity.Launch", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The undo half of the fold (<c>SetNotice</c>, <c>ShowNotices</c>, <c>ClearNotices</c>, and
    /// the <c>Show</c>/<c>Dismiss</c> undo machinery) is unchanged by D162 — only what a NOTICE
    /// fold turns to is different. A regression that also rewired the undo path would be a much
    /// larger, unintended change than this one, so it is worth a guard of its own.
    /// </summary>
    [Fact]
    public void TheUndoMachineryStillUsesFoldViewDirectlyRatherThanTheNewPage()
    {
        var source = File.ReadAllText(RepoFile("FocusPhone/Codex.Ui.Android/DogEar.cs"));

        var showMethod = Regex.Match(
            source,
            @"public static void Show\(Context context, FrameLayout host, string undoHint, Action onUndo\)\s*\{(?<body>.*?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(showMethod.Success, "Could not find DogEar.Show(...) (the undo fold) — has it moved or been renamed?");
        Assert.DoesNotContain("WaitingActivity", showMethod.Groups["body"].Value, StringComparison.Ordinal);
    }

    private static string OpenNoticesBody()
    {
        var source = File.ReadAllText(RepoFile("FocusPhone/Codex.Ui.Android/DogEar.cs"));

        var match = Regex.Match(
            source,
            @"private static void OpenNotices\(Context context, FrameLayout host\)\s*\{(?<body>.*?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(match.Success, "Could not find OpenNotices(...)'s body in DogEar.cs.");
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
