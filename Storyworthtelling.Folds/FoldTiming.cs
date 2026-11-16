namespace Storyworthtelling.Folds;

/// <summary>
/// How long an undo holds the corner (DESIGN.md §7, D50): six seconds, or longer when the person has asked Android
/// for more time to act (Accessibility → Time to take action; audit 10 §5, WCAG 2.2.1).
///
/// <para>The window is read at run time, never baked into a constant at a call site: the fold and whatever commits
/// when it closes (Post's source delete, Index's bank removal) must close together, or the undo is a lie. Each kit
/// <see cref="Adopt"/>s the platform's recommendation as it shows an undo, and every caller reads
/// <see cref="UndoWindow"/>.</para>
/// </summary>
public static class FoldTiming
{
    /// <summary>The design's window, the least any undo is given.</summary>
    public const int UndoWindowMs = 6_000;

    private static readonly TimeSpan Design = TimeSpan.FromMilliseconds(UndoWindowMs);
    private static long windowTicks = Design.Ticks;

    /// <summary>The window an undo opens with now: the design's, or the platform's recommendation if longer.</summary>
    public static TimeSpan UndoWindow => TimeSpan.FromTicks(Interlocked.Read(ref windowTicks));

    /// <summary>The window for a recommendation: never shorter than the design's six seconds.</summary>
    public static TimeSpan WindowFor(TimeSpan recommended) => recommended > Design ? recommended : Design;

    /// <summary>Takes the platform's recommendation for this process (a kit calls it before every undo).</summary>
    public static void Adopt(TimeSpan recommended) => Interlocked.Exchange(ref windowTicks, WindowFor(recommended).Ticks);
}
