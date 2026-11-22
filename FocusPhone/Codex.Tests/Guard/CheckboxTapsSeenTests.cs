using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// A checkbox row's toggle was judged dead: ticking the kit's drawn checkbox only invalidates it and changes its
/// state description, and the tap watch's layout fingerprint reads a screen's words, so a real change looked like
/// nothing at all. A change of the box's state now tells the watch. Head source, read by content.
/// </summary>
public class CheckboxTapsSeenTests
{
    [Fact]
    public void PickingTheCheckboxTellsTheWatch()
    {
        var source = LogLoopSource.File("FocusPhone/Codex.Ui.Android/Checkbox.cs");

        var set = Regex.Match(source, @"public void SetPicked\(bool value\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(set.Success, "The checkbox should have a SetPicked body.");
        var body = set.Groups["body"].Value;
        Assert.Contains("TapWatch.Status()", body, StringComparison.Ordinal);
        // Only a real change is a change: the early return for the same state comes first.
        Assert.True(
            body.IndexOf("return;", StringComparison.Ordinal) < body.IndexOf("TapWatch.Status()", StringComparison.Ordinal),
            "Setting the state it already had should not tell the watch anything changed.");
    }
}
