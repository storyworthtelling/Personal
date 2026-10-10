using Folio.Domain.Markup;

namespace Folio.Tests.Markup;

/// <summary>
/// Which line of the marked manuscript a scene's block follows.
///
/// <para>A scene's span usually ends in the blank space before the next scene's break, and the block
/// belongs under the scene's last <i>words</i>. Taking the line the raw end offset falls on would put
/// the block under the white space — or, when that space runs into the next scene's first line, under
/// the wrong scene.</para>
/// </summary>
public class SceneBlockPlanTests
{
    private const string Text = "One line.\nTwo line.\n\n* * *\n\nThree line.\n";

    // One span per text line, the way the page wraps it; blank lines are zero-length spans.
    private static IReadOnlyList<LineSpan> Lines()
    {
        var spans = new List<LineSpan>();
        var start = 0;
        foreach (var line in Text.Split('\n'))
        {
            spans.Add(new LineSpan(spans.Count, start, start + line.Length));
            start += line.Length + 1;
        }
        return spans;
    }

    [Fact]
    public void ASceneEndingOnItsLastWordsFollowsThatLine()
    {
        var end = Text.IndexOf("Two line.", StringComparison.Ordinal) + "Two line.".Length;

        Assert.Equal(1, SceneBlockPlan.LineAfter(Text, Lines(), end));
    }

    /// <summary>The span runs on through the blank lines and the break; the block still sits under "Two line."</summary>
    [Fact]
    public void TrailingWhitespaceInTheScenesSpanDoesNotPushTheBlockDown()
    {
        var end = Text.IndexOf("Three", StringComparison.Ordinal);

        Assert.Equal(3, SceneBlockPlan.LineAfter(Text, Lines(), end));   // "* * *" is the last text before it
    }

    [Fact]
    public void AnEndPastTheTextFollowsTheLastLineOfText()
    {
        // Line 5, "Three line." — not the empty line the trailing newline leaves after it.
        Assert.Equal(5, SceneBlockPlan.LineAfter(Text, Lines(), Text.Length + 500));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void AnUnknownEndHasNoLine(int end)
    {
        Assert.Null(SceneBlockPlan.LineAfter(Text, Lines(), end));
    }

    [Fact]
    public void NoLinesHasNoLine()
    {
        Assert.Null(SceneBlockPlan.LineAfter(Text, [], 5));
    }
}
