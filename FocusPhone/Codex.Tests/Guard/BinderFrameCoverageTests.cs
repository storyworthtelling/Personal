using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Every real screen in Score, Post and Plate wears the binder frame (DESIGN.md §9) — the
/// tab strip each app's top-level shell (<c>ScoreActivity</c>/<c>PostActivity</c>/
/// <c>PlateActivity</c>) already draws for its own areas, via <c>Codex.Ui.BinderFrame</c> for
/// the plain <c>Activity</c> screens those shells launch (an open email, the equalizer, a
/// diagnostics log, and so on).
///
/// <para><b>This is the guard the regression needed.</b> Eleven screens across three native
/// heads called <c>SetContentView</c> on a header-and-content layout of their own with no
/// binder gutter at all — reachable only by leaving the app's own navigation, so nothing
/// short of opening each one by hand would have noticed. Foreword shipped the identical
/// failure once already for its own (MAUI) screens — a binder strip built, tested, and wired
/// to nothing — which is why that head's <c>BinderShellTests</c>/Folio's <c>ScreenFrameTests</c>
/// read every page's source rather than trusting that a shared component being correct means
/// every screen reaches for it. This is that same check for the native heads: a future screen
/// that calls <c>SetContentView</c> without going through <see cref="Codex.Ui.BinderFrame"/>
/// fails here, by name, rather than shipping quietly frameless.</para>
/// </summary>
public class BinderFrameCoverageTests
{
    // The three app shells draw BinderTabs directly — they ARE the frame, not a screen
    // that wears it — so they're the one legitimate reason a file in these folders can use
    // BinderTabs without going through BinderFrame.
    private static readonly string[] Shells = ["ScoreActivity.cs", "PostActivity.cs", "PlateActivity.cs"];

    // Documented, deliberate exceptions to "every screen has the frame":
    //  - ErrandActivity: DESIGN.md's own carve-out — "there is no binder gutter on this
    //    screen, in any of its faces" — because another app is blocked in onActivityResult
    //    and there is nowhere else to go.
    //  - CallNoteShareActivity / SmsSendToActivity: routers, not screens. Neither calls
    //    SetContentView at all (they read the incoming intent and finish), so they would
    //    never trip this guard anyway; named for the same reason FrozenInvariantsTests
    //    names its own standing exceptions — so a change that gives one a real layout is a
    //    decision, not a drift.
    private static readonly string[] DocumentedExceptions =
        ["ErrandActivity.cs", "CallNoteShareActivity.cs", "SmsSendToActivity.cs"];

    private static readonly string[] Heads = ["Score.Android", "Post.Android", "Plate.Android"];

    private static readonly Regex ActivityClass = new(
        @"class\s+\w+\s*:\s*(Android\.App\.)?(AppCompat)?Activity\b", RegexOptions.Compiled);

    [Fact]
    public void EveryRealScreenUsesTheBinderFrame()
    {
        var offenders = new List<string>();

        foreach (var head in Heads)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "FocusPhone", head), "*.cs"))
            {
                var name = Path.GetFileName(file);
                if (Shells.Contains(name) || DocumentedExceptions.Contains(name))
                {
                    continue;
                }

                var source = File.ReadAllText(file);
                if (!ActivityClass.IsMatch(source) || !source.Contains("SetContentView(", StringComparison.Ordinal))
                {
                    continue; // not a real, content-drawing screen (a helper class, an Area, etc.)
                }

                if (!source.Contains("BinderFrame.", StringComparison.Ordinal))
                {
                    offenders.Add($"{head}/{name} calls SetContentView without going through "
                        + "Codex.Ui.BinderFrame, so it has no binder gutter and no in-page header rule.");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    // The parity half: ScoreShellAreas/PostShellAreas/PlateShellAreas are hand-kept copies of
    // each shell's own `areas = [...]` array (see those files' own remarks for why a copy
    // exists at all) — this is what stops the copy silently drifting out of order if a shell
    // ever adds, removes or reorders an area.
    [Theory]
    [InlineData("Score.Android", "ScoreActivity.cs",
        new[] { "MusicArea", "PodcastsArea", "PlayingArea", "DiscoverArea", "WorkoutArea", "SettingsArea" })]
    [InlineData("Post.Android", "PostActivity.cs",
        new[] { "MailboxArea", "FiledArea", "RolodexArea", "SettingsArea" })]
    [InlineData("Plate.Android", "PlateActivity.cs",
        new[] { "CameraArea", "ScanArea", "DarkroomArea", "SettingsArea" })]
    public void ShellAreaTablesMatchTheShellsOwnOrder(string head, string shellFile, string[] expectedOrder)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "FocusPhone", head, shellFile));
        // Distinguishes the populated assignment (OnCreate's multi-line `areas = [\n  new ...(),`)
        // from the field's own empty-array declaration (`private XArea[] areas = [];`), which also
        // matches a lazier pattern and would otherwise win as the FIRST "areas = [...]" in the file.
        var block = Regex.Match(source, @"areas\s*=\s*\[\s*\r?\n(?<body>.*?)\];", RegexOptions.Singleline);
        Assert.True(block.Success, $"{shellFile} no longer declares an `areas = [...]` array this guard can read.");

        var found = Regex.Matches(block.Groups["body"].Value, @"new\s+(\w+)\(")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.Equal(expectedOrder, found);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repo root (.github) above " + AppContext.BaseDirectory);
    }
}
