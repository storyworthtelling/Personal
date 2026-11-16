using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// DESIGN.md §6's motion across every head and both kits (audit W8.7, report 04 §21–24). The design has
/// three durations: Quick 150, Standard 200 and Ink 320. The code had two of them. Its page turns ran at
/// 240, Flyleaf's cover faded out over 450, the action cards revealed at Standard where §7 asks for Ink,
/// two of Pulp's screens never turned, and the MAUI apps switched areas and raised the dog-ear with no
/// motion at all.
///
/// <list type="bullet">
/// <item>An animation lasts one of the three durations: a token in code, one of the three numbers in an anim resource.</item>
/// <item>A paper card's haze arrives with the Ink reveal.</item>
/// <item>Every native screen that draws something turns like a page; a trampoline that finishes at once draws nothing to turn.</item>
/// <item>A MAUI area switch goes through the kit's page turn, and the kit's dog-ear folds in.</item>
/// </list>
/// </summary>
public partial class MotionTests
{
    private static readonly string[] Heads =
    [
        "FocusPhone/Codex.Ui.Android", "FocusPhone/Flyleaf.Android", "FocusPhone/Score.Android", "FocusPhone/Post.Android",
        "FocusPhone/Plate.Android", "Pulp/Pulp.Android", "Constitution/Constitution.Android", "Ledger/Ledger.Android",
        "Storyboarder/Folio.Android", "Foreword/Foreword.Android", "Storyworthtelling.Maui",
    ];

    private static readonly string[] NativeHeads =
    [
        "FocusPhone/Flyleaf.Android", "FocusPhone/Score.Android", "FocusPhone/Post.Android", "FocusPhone/Plate.Android",
        "Pulp/Pulp.Android", "Constitution/Constitution.Android",
    ];

    private static readonly string[] MauiHeads = ["Ledger/Ledger.Android", "Storyboarder/Folio.Android", "Foreword/Foreword.Android"];

    private static readonly int[] Durations = [150, 200, 320];

    [Fact]
    public void AnAnimationInCodeTakesAToken()
    {
        var found = Lines(Heads, ".cs")
            .Where(hit => CodedDuration().IsMatch(hit.line))
            .Select(hit => hit.at)
            .ToList();
        Assert.True(found.Count == 0, "Use DesignTokens.Motion (QuickMs, StandardMs, InkMs), not a number:\n" + string.Join("\n", found));
    }

    [Fact]
    public void AnAnimationResourceTakesOneOfTheThreeDurations()
    {
        var found = Lines(Heads, ".xml")
            .Where(hit => hit.at.Contains("/Resources/anim/", StringComparison.Ordinal))
            .SelectMany(hit => ResourceDuration().Matches(hit.line).Select(m => (hit.at, value: int.Parse(m.Groups["ms"].Value))))
            .Where(hit => !Durations.Contains(hit.value))
            .Select(hit => $"{hit.at} lasts {hit.value}")
            .ToList();
        Assert.True(found.Count == 0, "Use Quick 150, Standard 200 or Ink 320:\n" + string.Join("\n", found));
    }

    [Theory]
    [InlineData("FocusPhone/Codex.Ui.Android/ActionCard.cs")]
    [InlineData("Constitution/Constitution.Android/WorkoutsTab/WorkoutCardView.cs")]
    [InlineData("Constitution/Constitution.Android/MealsTab/CuisineCardView.cs")]
    public void ACardsHazeArrivesWithTheInkReveal(string file)
    {
        var text = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), file));
        Assert.Contains("scrim.Animate()!.Alpha(1f).SetDuration(DesignTokens.Motion.InkMs)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryNativeScreenTurnsLikeAPage()
    {
        var files = NativeHeads
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(GuardScan.RepoRoot(), folder), "*.cs", SearchOption.AllDirectories))
            .Where(path => !InBuildOutput(path))
            .ToList();

        // Each class to the file declaring it, so a screen may turn through its base.
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (Match m in ClassDeclaration().Matches(File.ReadAllText(file)))
            {
                declared.TryAdd(m.Groups["name"].Value, file);
            }
        }

        bool Turns(string file, int depth)
        {
            var text = File.ReadAllText(file);
            if (text.Contains("PaperTransitions.Apply(", StringComparison.Ordinal))
            {
                return true;
            }

            var bases = ClassDeclaration().Matches(text).Select(m => m.Groups["base"].Value);
            return depth < 4 && bases.Any(b => declared.TryGetValue(b, out var baseFile) && baseFile != file && Turns(baseFile, depth + 1));
        }

        var found = files
            .Where(file => ClassDeclaration().Matches(File.ReadAllText(file)).Any(m => m.Groups["base"].Value.EndsWith("Activity", StringComparison.Ordinal)))
            .Where(file => File.ReadAllText(file).Contains("SetContentView(", StringComparison.Ordinal))
            .Where(file => !Turns(file, 0))
            .Select(Relative)
            .ToList();
        Assert.True(found.Count == 0, "Call PaperTransitions.Apply(this) in OnCreate:\n" + string.Join("\n", found));
    }

    [Fact]
    public void AMauiAreaSwitchTurnsThePage()
    {
        var found = Lines(MauiHeads, ".cs")
            .Where(hit => hit.line.Contains("animated: false", StringComparison.Ordinal))
            .Select(hit => hit.at)
            .ToList();
        Assert.True(found.Count == 0, "Turn to an area through PaperBinderPage.TurnToAreaAsync:\n" + string.Join("\n", found));

        var kit = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), "Storyworthtelling.Maui/Views/PaperBinderPage.cs"));
        Assert.Contains("protected async Task TurnToAreaAsync(", kit, StringComparison.Ordinal);
        Assert.Contains("DesignTokens.Motion.StandardMs", kit, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMauiDogEarFoldsIn()
    {
        var ear = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), "Storyworthtelling.Maui/Views/DogEar.cs"));
        Assert.Contains("FadeTo(1, DesignTokens.Motion.StandardMs", ear, StringComparison.Ordinal);
        Assert.Contains("ScaleTo(1, DesignTokens.Motion.StandardMs", ear, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SetDuration(450)", true)]
    [InlineData("SetDuration(DesignTokens.Motion.InkMs)", false)]
    [InlineData("await view.FadeTo(1, 250);", true)]
    [InlineData("await view.FadeTo(1, DesignTokens.Motion.StandardMs, Easing.CubicOut);", false)]
    [InlineData("TranslateTo(0, 0, 180)", true)]
    public void TheCodeDetectorSeesANumber(string line, bool flagged) => Assert.Equal(flagged, CodedDuration().IsMatch(line));

    [GeneratedRegex(@"\bSetDuration\(\s*[0-9]|\b(FadeTo|ScaleTo|TranslateTo|RotateTo|LayoutTo)\([^)]*,\s*[0-9]+\s*[,)]")]
    private static partial Regex CodedDuration();

    [GeneratedRegex(@"android:duration=""(?<ms>[0-9]+)""")]
    private static partial Regex ResourceDuration();

    [GeneratedRegex(@"\bclass\s+(?<name>\w+)\s*:\s*(?<base>[\w.]+)")]
    private static partial Regex ClassDeclaration();

    private static IEnumerable<(string at, string line)> Lines(IEnumerable<string> folders, string extension) =>
        folders
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(GuardScan.RepoRoot(), folder), "*" + extension, SearchOption.AllDirectories))
            .Where(path => !InBuildOutput(path))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (at: $"{Relative(file)}:{index + 1}", line)))
            .Where(hit => !hit.line.TrimStart().StartsWith("//", StringComparison.Ordinal));

    private static bool InBuildOutput(string path) => path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj");

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');
}
