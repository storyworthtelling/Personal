using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// DESIGN.md §17 across both kits and every head (audit W9.1, report 10 §2–6). Before this, a screen reader heard no
/// headings, no status sentence changing, no dog-ear arriving, no checked box, no selected tab, no disabled segment,
/// and secondary text sat at 2.8:1 on the grain.
///
/// <list type="bullet">
/// <item>Words are never InkMuted: secondary, label, hint and placeholder text is InkSoft (owner's ruling). InkMuted is
/// for strokes — a pencil outline, a chevron, a divider. A struck or disabled line may stay muted.</item>
/// <item>Screen headers are headings; status and problem lines are live regions.</item>
/// <item>The dog-ear is announced, is a 48dp target, and its window honours "Time to take action" through
/// <c>FoldTiming</c>, read at run time by everything that commits when it closes.</item>
/// <item>Checkboxes, tabs and segments say their state; a disabled control is <c>Enabled = false</c>, not only faint.</item>
/// <item>A drawn view a person can tap describes itself.</item>
/// </list>
/// </summary>
public partial class AccessibilityTests
{
    private static readonly string[] Screens =
    [
        "FocusPhone/Codex.Ui.Android", "FocusPhone/Flyleaf.Android", "FocusPhone/Score.Android", "FocusPhone/Post.Android",
        "FocusPhone/Plate.Android", "Pulp/Pulp.Android", "Constitution/Constitution.Android", "Ledger/Ledger.Android",
        "Storyboarder/Folio.Android", "Foreword/Foreword.Android", "Storyworthtelling.Maui",
    ];

    private const string NativeKit = "FocusPhone/Codex.Ui.Android";
    private const string MauiKit = "Storyworthtelling.Maui";

    [Fact]
    public void WordsAreNeverInkMuted()
    {
        // A struck line (done, crossed through) may stay muted; it says so on its own line or the one before.
        var found = Lines(Screens, ".cs", ".xaml")
            .Where(hit => hit.line.Contains("InkMuted", StringComparison.Ordinal))
            .Where(hit => TextSink().IsMatch(hit.line))
            .Where(hit => !Regex.IsMatch(hit.previous + hit.line, "struck|Struck|Strikethrough|enabled|Enabled"))
            .Select(hit => hit.at)
            .ToList();
        Assert.True(found.Count == 0, "Secondary text is InkSoft (4.5:1); InkMuted is for strokes:\n" + string.Join("\n", found));
    }

    [Theory]
    [InlineData("Display")]
    [InlineData("Title")]
    [InlineData("Heading")]
    [InlineData("ScreenHeader")]
    public void ANativeHeaderIsAHeading(string method) =>
        Assert.Contains("Headed(", Body(Read(NativeKit, "PaperUi.cs"), method), StringComparison.Ordinal);

    [Fact]
    public void TheNativeKitMarksHeadingsAndLiveLines()
    {
        var kit = Read(NativeKit, "PaperUi.cs");
        Assert.Contains("AccessibilityHeading = true", kit, StringComparison.Ordinal);
        Assert.Contains("AccessibilityLiveRegion", Body(kit, "StatusLine"), StringComparison.Ordinal);
        Assert.Contains("AccessibilityLiveRegion", Body(kit, "ProblemLine"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMauiKitMarksHeadingsAndLiveLines()
    {
        var semantics = Read(MauiKit, "Materials/PaperSemantics.cs");
        Assert.Contains("\"DisplayLabel\"", semantics, StringComparison.Ordinal);
        Assert.Contains("AccessibilityHeading = true", semantics, StringComparison.Ordinal);
        Assert.Contains("\"StatusLine\"", semantics, StringComparison.Ordinal);
        Assert.Contains("\"ErrorLabel\"", semantics, StringComparison.Ordinal);
        Assert.Contains("AccessibilityLiveRegion", semantics, StringComparison.Ordinal);
        Assert.Contains("PaperSemantics.Install()", Read(MauiKit, "Resources/Styles/Paper.xaml.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NativeKit, "DogEar.cs")]
    [InlineData(MauiKit, "Views/DogEar.cs")]
    public void TheDogEarIsAnnouncedReachableAndPatient(string kit, string file)
    {
        var ear = Read(kit, file);
        Assert.Matches("AnnounceForAccessibility|SemanticScreenReader.Announce", ear);
        Assert.Contains("GetRecommendedTimeoutMillis", ear, StringComparison.Ordinal);
        Assert.Contains("FoldTiming.Adopt(", ear, StringComparison.Ordinal);
        Assert.Contains("TouchTargetDp", ear, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingTimesAnUndoByTheConstant()
    {
        var found = Lines(["FocusPhone", "Pulp", "Constitution", "Ledger", "Storyboarder", "Foreword", MauiKit], ".cs")
            .Where(hit => hit.line.Contains("UndoWindowMs", StringComparison.Ordinal))
            .Where(hit => !hit.line.Contains("const int UndoWindowMs", StringComparison.Ordinal))
            .Where(hit => !hit.at.Contains("Tests/", StringComparison.Ordinal))
            // The dog-ears hand the design's window to Android as the time to be lengthened.
            .Where(hit => !hit.at.Contains("/DogEar.cs:", StringComparison.Ordinal))
            .Select(hit => hit.at)
            .ToList();
        Assert.True(found.Count == 0, "Read FoldTiming.UndoWindow, which honours Time to take action:\n" + string.Join("\n", found));
    }

    [Fact]
    public void ControlsSayTheirState()
    {
        Assert.Contains("Checkable", Read(NativeKit, "Checkbox.cs"), StringComparison.Ordinal);
        Assert.Contains("Selected =", Read(NativeKit, "BinderTabs.cs"), StringComparison.Ordinal);

        var segmented = Body(Read(NativeKit, "PaperUi.cs"), "Segmented");
        Assert.Contains("Selected =", segmented, StringComparison.Ordinal);
        Assert.Contains("Enabled = false", segmented, StringComparison.Ordinal);

        Assert.Contains("SemanticProperties.SetDescription", Read(MauiKit, "Views/BinderTabs.cs"), StringComparison.Ordinal);
        Assert.Contains("SemanticProperties.SetDescription", Read(MauiKit, "Views/PaperSegmented.cs"), StringComparison.Ordinal);
        Assert.Contains("SemanticProperties.SetDescription", Read(MauiKit, "Views/PaperList.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ADisabledControlIsDisabledNotOnlyFaint()
    {
        var found = Lines(Screens, ".cs")
            .Where(hit => hit.line.Contains("DisabledOpacity", StringComparison.Ordinal) && !hit.line.Contains("const ", StringComparison.Ordinal))
            .Where(hit => !hit.line.Contains("struck", StringComparison.Ordinal))
            // A row faded to say something about it (not out yet, caught up) is still a live row, and says so.
            .Where(hit => !hit.previous.Contains("not disabled", StringComparison.Ordinal))
            .Where(hit => !File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), hit.at[..hit.at.LastIndexOf(':')])).Contains("Enabled = false", StringComparison.Ordinal))
            .Select(hit => hit.at)
            .ToList();
        Assert.True(found.Count == 0, "Set Enabled = false beside the dimming, so TalkBack says disabled:\n" + string.Join("\n", found));
    }

    /// <summary>Drawn views still to be given a node per item; each goes in its own PR and leaves this list.</summary>
    private static readonly string[] StillToDescribe =
    [
        "FocusPhone/Codex.Ui.Android/Dial.cs",
        "FocusPhone/Plate.Android/CropOverlayView.cs",
    ];

    [Fact]
    public void ATappableDrawnViewDescribesItself()
    {
        var found = Screens
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(GuardScan.RepoRoot(), folder), "*.cs", SearchOption.AllDirectories))
            .Where(path => !InBuildOutput(path))
            .Where(path => DrawnView().IsMatch(File.ReadAllText(path)))
            .Where(path => Tappable().IsMatch(File.ReadAllText(path)))
            .Where(path => !Described().IsMatch(File.ReadAllText(path)))
            .Select(Relative)
            .Where(path => !StillToDescribe.Contains(path))
            .ToList();
        Assert.True(found.Count == 0, "A drawn view a person can tap needs a description or a node per item:\n" + string.Join("\n", found));
    }

    /// <summary>
    /// Flyleaf's shelf is the only home on the locked phone (audit W9.2): its books, clock and bookends are nodes, and a
    /// finger hits the same widened boxes TalkBack reads.
    /// </summary>
    [Fact]
    public void TheShelfIsReadableAndTappableThroughOneSetOfNodes()
    {
        var shelf = Read("FocusPhone/Flyleaf.Android", "BookshelfView.cs");
        Assert.Contains("AccessibilityNodeProvider? AccessibilityNodeProvider => provider", shelf, StringComparison.Ordinal);
        Assert.Contains("ShelfAccess.Nodes(layout, DesignTokens.Shape.TouchTargetDp * density", shelf, StringComparison.Ordinal);
        Assert.Contains("ShelfAccess.At(nodes, x, y)", shelf, StringComparison.Ordinal);
        Assert.Contains("DispatchHoverEvent", shelf, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SetTextColor(PaperTheme.InkMuted);", true)]
    [InlineData("<Setter Property=\"PlaceholderColor\" Value=\"{StaticResource InkMuted}\" />", true)]
    [InlineData("TextColor = PaperResources.Colour(\"InkMuted\"),", true)]
    [InlineData("MaterialPainters.Pencil(stroke, PaperTheme.InkMuted, density);", false)]
    public void TheTextDetectorSeesText(string line, bool flagged) =>
        Assert.Equal(flagged, TextSink().IsMatch(line) && line.Contains("InkMuted", StringComparison.Ordinal));

    [GeneratedRegex(@"SetTextColor|SetHintTextColor|TextColor\b|PlaceholderColor|TypeSp\.(Secondary|Label|Body|Mono)|\bText\(context")]
    private static partial Regex TextSink();

    [GeneratedRegex(@"class\s+\w+\s*:\s*(View|GraphicsView)\b")]
    private static partial Regex DrawnView();

    [GeneratedRegex(@"OnTouchEvent|TapGestureRecognizer|GestureDetector")]
    private static partial Regex Tappable();

    [GeneratedRegex(@"ContentDescription|OnInitializeAccessibilityNodeInfo|ExploreByTouchHelper|SemanticProperties")]
    private static partial Regex Described();

    private static string Read(string folder, string file) => File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), folder, file));

    /// <summary>A method's text from its signature to the next public member, enough to ask what it sets.</summary>
    private static string Body(string source, string method)
    {
        var start = Regex.Match(source, $@"public static \S+ {method}\(");
        Assert.True(start.Success, $"{method} has moved");
        var next = source.IndexOf("\n    public static ", start.Index + start.Length, StringComparison.Ordinal);
        return source[start.Index..(next < 0 ? source.Length : next)];
    }

    private static IEnumerable<(string at, string line, string previous)> Lines(IEnumerable<string> folders, params string[] extensions) =>
        folders
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(GuardScan.RepoRoot(), folder), "*", SearchOption.AllDirectories))
            .Where(path => extensions.Contains(Path.GetExtension(path)) && !InBuildOutput(path))
            .Where(path => !path.Contains("/Infrastructure/", StringComparison.Ordinal) && !path.Contains(".Infrastructure/", StringComparison.Ordinal))
            .SelectMany(file =>
            {
                var lines = File.ReadAllLines(file);
                return lines.Select((line, index) => (at: $"{Relative(file)}:{index + 1}", line, previous: index > 0 ? lines[index - 1] : string.Empty));
            })
            .Where(hit => !hit.line.TrimStart().StartsWith("//", StringComparison.Ordinal));

    private static bool InBuildOutput(string path) => path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj");

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');
}
