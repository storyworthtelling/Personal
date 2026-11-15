using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// The tap watch judges a tap by whether the view under the finger is a control, which it reads off Android's
/// Clickable flags. A view that handles its own touches (OnTouchEvent, a GestureDetector) and draws its controls
/// on a canvas — Flyleaf's bookshelf, whose books are painted — is not Clickable, so every tap on a book read as
/// a tap on a non-control. Such a view says where its controls are through <c>IDrawnControls</c>, and the watch
/// asks it. The heads cannot be loaded here, so this reads their source.
/// </summary>
public class DrawnControlsTests
{
    // Views that consume touches but are drag surfaces rather than tap targets, whose result is drawn, not laid
    // out: judging a tap on one as a control would make it a dead-tap candidate the watch cannot see answered.
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        ["FocusPhone/Codex.Ui.Android/Dial.cs"] = "a dial turned by dragging; its reading is drawn on the canvas",
        ["FocusPhone/Plate.Android/CropOverlayView.cs"] = "crop handles dragged over a photo; a tap does nothing by design",
    };

    private static readonly string[] HeadFolders = ["FocusPhone", "Pulp", "Constitution"];

    [Fact]
    public void EveryViewThatHandlesItsOwnTouchesSaysWhereItsControlsAre()
    {
        var offenders = TouchHandlingViews()
            .Where(v => !Exceptions.ContainsKey(v.Path))
            .Where(v => !Regex.IsMatch(v.Text, @"class \w+\s*:[^{]*\bIDrawnControls\b"))
            .Select(v => v.Path)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These views handle their own touches without implementing IDrawnControls, so the tap watch cannot "
            + "tell a tap on what they draw from a tap on nothing: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheBookshelfIsOneOfThem()
    {
        Assert.Contains(TouchHandlingViews(), v => v.Path == "FocusPhone/Flyleaf.Android/BookshelfView.cs");
        var shelf = LogLoopSource.File("FocusPhone/Flyleaf.Android/BookshelfView.cs");

        Assert.Matches(new Regex(@"public bool HasControlAt\(float x, float y\)\s*=>\s*HitTest\(x, y\) is not null;"), shelf);
    }

    [Fact]
    public void EveryExceptionStillHandlesItsOwnTouches()
    {
        var views = TouchHandlingViews().Select(v => v.Path).ToHashSet(StringComparer.Ordinal);

        Assert.All(Exceptions.Keys, path => Assert.Contains(path, views));
    }

    [Fact]
    public void TheWatchAsksADrawnViewInItsOwnCoordinates()
    {
        var watch = LogLoopSource.File("FocusPhone/Codex.Ui.Android/Diagnostics/TapWatch.cs");

        Assert.Contains("view is IDrawnControls drawn", watch, StringComparison.Ordinal);
        Assert.Contains("drawn.HasControlAt(x - origin[0], y - origin[1])", watch, StringComparison.Ordinal);
        Assert.Contains("interface IDrawnControls", LogLoopSource.File("FocusPhone/Codex.Ui.Android/Diagnostics/IDrawnControls.cs"), StringComparison.Ordinal);
    }

    private static IEnumerable<(string Path, string Text)> TouchHandlingViews() =>
        HeadFolders
            .SelectMany(LogLoopSource.Sources)
            .Where(s => !s.Path.Contains(".Tests/", StringComparison.Ordinal))
            .Where(s => Regex.IsMatch(s.Text, @"override bool OnTouchEvent\(MotionEvent\??\s+\w+\)"));
}
