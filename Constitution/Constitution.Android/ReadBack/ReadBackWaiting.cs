using Android.Content;
using Constitution.Infrastructure.ReadBack;
using Constitution.Settings;

namespace Constitution.ReadBack;

/// <summary>
/// The read-back's questions on the Waiting page. The board is in memory and the worker may have asked in a process that
/// is gone, so the questions are saved with the result and put back each time the app opens; a chip picked is remembered
/// as the answer so the question is not put back again.
/// </summary>
public static class ReadBackWaiting
{
    /// <summary>States every unanswered saved question to the board. A file that cannot be read is told to the trail, not thrown at the screen.</summary>
    public static void Restore(Context context)
    {
        try
        {
            var results = ReadBackServices.Results(context);
            ConnectionNoticeBoard.With(board => ReadBackQuestions.Raise(board, results));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            new ReadBackLog(context).Outcome($"ink read back questions not restored: {e.GetType().Name}");
        }
    }

    /// <summary>The owner picked a chip for the question filed under this cause; the pick is kept and the question is not asked again.</summary>
    public static void Answered(Context context, string causeKey, string chosen)
    {
        try
        {
            ReadBackServices.Results(context).MarkAnswered(causeKey, chosen);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            new ReadBackLog(context).Outcome($"ink read back questions not stored: {e.GetType().Name}");
        }
    }
}
