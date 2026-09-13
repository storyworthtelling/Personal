using System.Text.RegularExpressions;
using Xunit;

namespace Folio.Tests.Guard;

/// <summary>
/// Every act that takes something away offers it back, and none of them ask first.
///
/// <para><b>This records a deliberate departure from the approved plan.</b> Release D's line reads
/// "replace and repaint (a repaint asks first when the owner has replaced a cover)" — a reasonable
/// instinct, protecting a jacket somebody chose from being painted over. The owner then ruled out
/// confirmation prompts, and the intent survives without one: every jacket is written to its own
/// file and none is overwritten, so the previous cover is still on disk and the fold genuinely puts
/// it back. A prompt guesses in advance; an undo is the answer given afterwards, by the person who
/// can see what happened.</para>
///
/// <para>That only holds while the bytes outlive the row — the same conclusion Plate reached about
/// its darkroom. <c>CoverTests</c> holds the storage half; this holds the interface half.</para>
/// </summary>
public class UndoNotPromptTests
{
    /// <summary>
    /// Each of these changes what is on the page, and each is reversible, so each must hand the fold
    /// something to reverse it with. Named one at a time: adding an act to this app should mean
    /// deciding, here, how it is taken back.
    /// </summary>
    public static TheoryData<string> ReversibleActs =>
        ["Shelve", "ReplaceCover", "RepaintCover"];

    [Theory]
    [MemberData(nameof(ReversibleActs))]
    public void EveryReversibleActOffersItsUndo(string act)
    {
        var source = Source();
        var body = Body(source, act);

        Assert.True(
            body.Contains("OfferUndo(", StringComparison.Ordinal),
            $"{act} changes the page and can be taken back, but hands the dog-ear nothing to take it "
            + "back with. §6: act and offer the undo.");
    }

    [Theory]
    [MemberData(nameof(ReversibleActs))]
    public void NoReversibleActAsksFirst(string act)
    {
        var body = Body(Source(), act);

        Assert.DoesNotContain("DisplayAlert", body, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayActionSheet", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The undo has to have something to restore <b>from</b>. Each of these reads the previous state
    /// before changing it, which is what makes the fold's promise true rather than hopeful.
    /// </summary>
    [Theory]
    [InlineData("ReplaceCover")]
    [InlineData("RepaintCover")]
    public void ARepaintCapturesTheJacketItIsReplacing(string act)
    {
        var body = Body(Source(), act);

        Assert.Matches(new Regex(@"\(was,\s*wasOwners\)\s*=\s*\(status\.CoverPath,\s*status\.CoverIsOwners\)"), body);
        Assert.Contains("RestoreCover(status.Id, was, wasOwners)", body, StringComparison.Ordinal);
    }

    private static string Source() =>
        FolioInvariantsTests.HeadFiles()
            .Select(File.ReadAllText)
            .First(text => text.Contains("private async Task RepaintCover(", StringComparison.Ordinal));

    // The method's body: from its signature to the start of the next one at the same indent.
    private static string Body(string source, string method)
    {
        var start = source.IndexOf($" Task {method}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No method named {method} on the Work desk.");

        var next = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }
}
