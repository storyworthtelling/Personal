namespace Storyworthtelling.Folds;

/// <summary>
/// How long an undo holds the corner (DESIGN.md §7, D50): six seconds, everywhere. Each app's
/// <c>DesignTokens.Motion.UndoWindowMs</c> reads this one, so the window is written once.
/// </summary>
public static class FoldTiming
{
    public const int UndoWindowMs = 6_000;

    public static TimeSpan UndoWindow { get; } = TimeSpan.FromMilliseconds(UndoWindowMs);
}
