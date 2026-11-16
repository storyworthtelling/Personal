namespace Folio.Domain.Markup;

/// <summary>One corner of a leader, in page points.</summary>
public readonly record struct LeaderPoint(double X, double Y);

/// <summary>
/// The path a note's leader takes back to the phrase it is about (R6).
///
/// <para>Out of the note to the gutter, along the gutter to the anchor line, back along the white
/// between that line and the next, and up to the foot of the phrase's first word. Every stretch is level
/// or upright, because a diagonal across a column of prose reads as a mark on the prose. The run under
/// the line sits below a query's underline (two points under the baseline), so it is not read as a
/// second one, and above the next line's capitals.</para>
///
/// <para>The leader used to stop at the line's right edge and was drawn only when a note had been pushed
/// down. With two or three notes on one line that left the reader guessing which phrase each meant, so
/// every note now has one, and two notes on one line run back at different levels.</para>
/// </summary>
public static class LeaderRoute
{
    /// <summary>How far under the baseline the run back along the line sits.</summary>
    public const double Drop = 4.5;

    /// <summary>How far apart two leaders on one line run.</summary>
    public const double StackStep = 1.4;

    /// <summary>The deepest a run goes, so a crowded line never pushes one into the next line.</summary>
    public const int MaxStack = 2;

    /// <param name="phraseLeft">Where the anchored phrase starts on its first line.</param>
    /// <param name="phraseRight">Where that run ends (equal to the left for a caret).</param>
    /// <param name="baseline">The anchor line's baseline.</param>
    /// <param name="gutterX">The upright between the text and the margin.</param>
    /// <param name="noteLeft">Where the leader leaves the note.</param>
    /// <param name="noteY">The height it leaves at — the note's first line.</param>
    /// <param name="stack">Which note this is among those anchored to the same line, from zero.</param>
    public static IReadOnlyList<LeaderPoint> For(
        double phraseLeft, double phraseRight, double baseline, double gutterX, double noteLeft, double noteY, int stack)
    {
        var level = baseline + Drop + (Math.Clamp(stack, 0, MaxStack) * StackStep);

        // Just inside the phrase, so the leader visibly lands on its first letter rather than its edge.
        var foot = phraseLeft + Math.Min(1.5, Math.Max(phraseRight - phraseLeft, 0) / 2);
        return
        [
            new(noteLeft, noteY),
            new(gutterX, noteY),
            new(gutterX, level),
            new(foot, level),
            new(foot, baseline + 1),
        ];
    }
}
