using Storyboarder.Domain.Analysis;
using Xunit;

namespace Storyboarder.Tests.Analysis;

public class DialogueFitTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyDialogue_IsReturnedUnchanged(string? dialogue)
    {
        Assert.Equal(dialogue, DialogueFit.Trim(dialogue));
    }

    [Fact]
    public void DialogueWithinBudget_IsUnchanged_AndTrimmedOfSurroundingSpace()
    {
        Assert.Equal("At last, the gate.", DialogueFit.Trim("  At last, the gate.  "));
    }

    [Fact]
    public void LongDialogue_IsCutToBudget_AtAWordBoundary_WithAnEllipsis()
    {
        var speech = string.Join(" ", Enumerable.Repeat("word", 100)); // well over the default budget

        var trimmed = DialogueFit.Trim(speech)!;

        Assert.True(trimmed.Length <= DialogueFit.DefaultMaxChars);
        Assert.EndsWith("…", trimmed);
        // Cut at a boundary: no partial word before the ellipsis.
        Assert.EndsWith("word…", trimmed);
        Assert.StartsWith("word word", trimmed);
    }

    [Fact]
    public void CustomBudget_IsHonoured()
    {
        var trimmed = DialogueFit.Trim("The hero drew a slow breath and spoke.", maxChars: 12)!;
        Assert.True(trimmed.Length <= 12);
        Assert.EndsWith("…", trimmed);
    }
}
