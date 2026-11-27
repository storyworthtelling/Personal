using Android.Util;

namespace Dash.Wear;

/// <summary>
/// Dash's one log, in logcat under one tag (plan §5): the contract and the outcome (<c>DashLogLines</c>), or a plain
/// sentence the code wrote, and never a name, a number, a word or a key. A failure is written by its type and frames,
/// never its message, which can carry whatever the call was given. <c>DashLogsCarryNoPayloadTests</c> holds every call.
/// </summary>
internal static class DashLog
{
    private const string Tag = "Dash"; // not copy: a logcat tag

    public static void Info(string line) => Log.Info(Tag, line);

    public static void Warn(string line) => Log.Warn(Tag, line);

    public static void Quiet(string line, Exception failure) => Log.Warn(Tag, Shape(line, failure));

    private static string Shape(string line, Exception failure) =>
        $"{line}: {failure.GetType().FullName}{System.Environment.NewLine}{failure.StackTrace}";
}
