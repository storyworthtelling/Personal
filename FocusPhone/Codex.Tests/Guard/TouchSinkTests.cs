using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// The action card swallows taps on its body so they do not fall through and dismiss it (log loop, C10). It did
/// so with an empty click handler, which made the tap watch take the whole card for a control: every tap on its
/// blank area was judged a dead button and named by the card's first text — its title, often a correspondent.
/// The card and its haze are now <c>ITouchSink</c> views, which the tap watch names by type and never judges as
/// a control.
/// </summary>
public class TouchSinkTests
{
    private const string Kit = "FocusPhone/Codex.Ui.Android";

    [Fact]
    public void TheActionCardsCardAndHazeAreTouchSinksWithNoEmptyClickHandler()
    {
        var card = LogLoopSource.File($"{Kit}/ActionCard.cs");

        Assert.DoesNotMatch(new Regex(@"\.Click\s*\+=\s*\(_,\s*_\)\s*=>\s*\{\s*\}"), card);
        Assert.Matches(new Regex(@"var scrim = new TouchSinkFrame\("), card);
        Assert.Matches(new Regex(@"var card = new TouchSinkColumn\("), card);
    }

    [Fact]
    public void TheTouchSinkViewsCarryTheMarker()
    {
        var sinks = LogLoopSource.File($"{Kit}/Diagnostics/TouchSink.cs");

        Assert.Contains("public interface ITouchSink", sinks, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"class TouchSinkFrame\b[^{;]*:\s*FrameLayout\b[^{;]*ITouchSink"), sinks);
        Assert.Matches(new Regex(@"class TouchSinkColumn\b[^{;]*:\s*LinearLayout\b[^{;]*ITouchSink"), sinks);
    }

    [Fact]
    public void TheTapWatchStopsAtASinkAndNamesItByTypeOnly()
    {
        var watch = LogLoopSource.File($"{Kit}/Diagnostics/TapWatch.cs");

        Assert.Contains("is ITouchSink", watch, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"ControlName\(null,\s*null,\s*\w+\.GetType\(\)\.Name\)"), watch);
    }
}
