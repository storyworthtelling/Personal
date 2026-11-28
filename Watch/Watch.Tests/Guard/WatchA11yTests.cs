using System.Text.RegularExpressions;

namespace Watch.Tests.Guard;

/// <summary>
/// WD §12 and P14 W-1.2.1: every view the watch kit draws describes itself to TalkBack, and a view a
/// finger can tap also offers that act to TalkBack (after <c>RepoGuards.Tests/AccessibilityTests</c>).
/// The guard reads <c>Watch/Watch.Ui.Wear</c> and Dash's own views by content, so it runs with no Android workload.
/// </summary>
public partial class WatchA11yTests
{
    private static readonly string[] Kit = ["WaveView", "DashGlyphView", "RingView", "UndoRingView", "WatchTextView", "WatchPillView"];

    // Dash's own drawn views (P14 B4): the rule reads the apps as well as the kit.
    private static readonly string[] DashViews = ["PersonRowView", "PhotoView", "ContactRowView", "MailRowView"];

    [Fact]
    public void TheGuardSeesTheKit()
    {
        var views = Views().Select(view => view.Name).ToList();

        Assert.All(Kit, name => Assert.Contains(name, views));
    }

    [Fact]
    public void TheGuardSeesDash()
    {
        var views = Views().Where(view => view.Path.StartsWith(Path.Combine("Watch", "Dash.Wear"), StringComparison.Ordinal)).Select(view => view.Name).ToList();

        Assert.All(DashViews, name => Assert.Contains(name, views));
    }

    [Fact]
    public void EveryViewDescribesItself()
    {
        var found = Views()
            .Where(view => !Described().IsMatch(view.Source))
            .Select(view => $"{view.Path} ({view.Name})")
            .ToList();

        Assert.True(found.Count == 0, "A watch view needs a content description or an accessibility role:\n" + string.Join("\n", found));
    }

    [Fact]
    public void EveryViewSaysWhatItIs()
    {
        var found = Views()
            .Where(view => !Role().IsMatch(view.Source))
            .Select(view => $"{view.Path} ({view.Name})")
            .ToList();

        Assert.True(found.Count == 0, "A watch view needs OnInitializeAccessibilityNodeInfo naming its class:\n" + string.Join("\n", found));
    }

    [Fact]
    public void ATappableViewCanBeTappedByTalkBack()
    {
        var found = Views()
            .Where(view => Tappable().IsMatch(view.Source))
            .Where(view => !Clickable().IsMatch(view.Source))
            .Select(view => $"{view.Path} ({view.Name})")
            .ToList();

        Assert.True(found.Count == 0, "A view that reads touches must also PerformClick or offer an accessibility action:\n" + string.Join("\n", found));
    }

    private static IEnumerable<(string Path, string Name, string Source)> Views()
    {
        var root = WatchFiles.RepoRoot();
        var folders = new[] { Path.Combine(root, "Watch", "Watch.Ui.Wear"), Path.Combine(root, "Watch", "Dash.Wear") }.Where(Directory.Exists);
        foreach (var path in folders.SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)).Where(path => !WatchFiles.IsBuildOutput(path)))
        {
            var source = File.ReadAllText(path);
            foreach (Match match in ViewClass().Matches(source))
            {
                yield return (Path.GetRelativePath(root, path), match.Groups["name"].Value, source);
            }
        }
    }

    [GeneratedRegex(@"class\s+(?<name>\w+)\s*:\s*(?:Android\.Views\.)?(?:View|TextView|ImageView|FrameLayout)\b")]
    private static partial Regex ViewClass();

    [GeneratedRegex(@"ContentDescription\s*=|OnInitializeAccessibilityNodeInfo|ExploreByTouchHelper")]
    private static partial Regex Described();

    [GeneratedRegex(@"OnInitializeAccessibilityNodeInfo[\s\S]*?ClassName\s*=")]
    private static partial Regex Role();

    [GeneratedRegex(@"OnTouchEvent|GestureDetector|OnGenericMotionEvent")]
    private static partial Regex Tappable();

    [GeneratedRegex(@"PerformClick\(|AccessibilityAction|PerformAccessibilityAction")]
    private static partial Regex Clickable();
}
