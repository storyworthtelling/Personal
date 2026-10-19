using Constitution.Domain.Ink;
using Constitution.Domain.Notices;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// Puts the questions the read-back saved back on the Waiting page. The notice board lives as long as the process, and
/// the worker that asked a question may have run in one that is long gone, so a question is only certain to be waiting
/// if it is saved with the result and restored from there when the app opens. Each keeps the date it was read on.
/// </summary>
public static class ReadBackQuestions
{
    /// <summary>States every unanswered question to the board; returns how many were not already waiting.</summary>
    public static int Raise(NoticeBoard board, IResolutionStore results)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(results);

        var raised = 0;
        foreach (var pending in results.PendingQuestions())
        {
            raised += InkAsks.Raise(board, [pending.Question], pending.ReadAt);
        }

        return raised;
    }
}
