using Constitution.Domain.Notices;

namespace Constitution.Domain.Ink;

/// <summary>
/// Carries the resolver's questions to the Waiting page (C-2.2.1): each <see cref="InkResolution.NeedsAsking"/>
/// becomes a <see cref="NoticeKind.LowConfidenceRead"/> notice, Caution in the fold, with the two answers as
/// its chips (<see cref="Waiting.WaitingPageModel"/> reads them from the cause key).
/// </summary>
public static class InkAsks
{
    /// <summary>
    /// Raises one notice per question. A question already waiting is not raised again (a notice is keyed
    /// by its cause), which also means two questions with the same subject and the same two answers on
    /// different pages share one notice; the read-back worker keys what it stores by page as well.
    /// Returns how many were newly raised.
    /// </summary>
    public static int Raise(NoticeBoard board, IEnumerable<InkResolution> resolutions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(resolutions);

        var raised = 0;
        foreach (var ask in resolutions.OfType<InkResolution.NeedsAsking>())
        {
            if (board.Raise(NoticeKind.LowConfidenceRead, ask.CauseKey, now))
            {
                raised++;
            }
        }

        return raised;
    }
}
