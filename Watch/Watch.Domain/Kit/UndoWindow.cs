using Storyworthtelling.Folds;

namespace Watch.Domain.Kit;

/// <summary>
/// The watch's undo (WD §9, O5; P14 W-1.2.1): six seconds, or Android's "Time to take action" when it is
/// longer, the same rule as the phones' dog-ear (<see cref="FoldTiming"/>), and a ring that empties as
/// the window runs out. When it closes, the act is final.
/// </summary>
public static class UndoWindow
{
    /// <summary>The window for the platform's recommended timeout: never shorter than six seconds.</summary>
    public static TimeSpan For(TimeSpan recommended) => FoldTiming.WindowFor(recommended);

    /// <summary>How much of the ring is left, 1 when it opens and 0 when it closes.</summary>
    public static double Remaining(TimeSpan elapsed, TimeSpan window) =>
        window <= TimeSpan.Zero ? 0 : Math.Clamp(1 - elapsed / window, 0, 1);

    /// <summary>Whether the act can still be taken back.</summary>
    public static bool IsOpen(TimeSpan elapsed, TimeSpan window) => elapsed < window;
}
