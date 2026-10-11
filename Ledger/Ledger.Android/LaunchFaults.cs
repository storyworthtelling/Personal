using global::Android.Util;
using Index.Domain.Diagnostics;
using Index.Domain.Identity;

namespace Index.Android;

/// <summary>
/// The one place a fault is recorded, so nothing that goes wrong at launch is lost. Each fault goes to
/// three places: the Android log, the rolling <c>crash.log</c> history, and <c>pending-crash.txt</c>,
/// which the next launch shows in full before the owner reaches the first page (Score's CrashReporter,
/// the same idea). Recording is best-effort only in the sense that a failure to write it is itself
/// logged to the Android log, never allowed to throw from inside a crash handler and mask the fault.
/// </summary>
internal static class LaunchFaults
{
    private const string CrashLogFileName = "crash.log";

    private static string FilesPath => global::Android.App.Application.Context.FilesDir!.AbsolutePath;

    internal static PendingCrashStore Pending() => new(FilesPath);

    /// <summary>Records a fault and returns the report text it wrote.</summary>
    internal static string Record(string where, string detail)
    {
        var text = LaunchFault.Describe(DateTimeOffset.Now, where, detail);
        Log.Error(IndexIdentity.DisplayName, text);
        try
        {
            File.AppendAllText(Path.Combine(FilesPath, CrashLogFileName), text + "\n\n---\n\n");
            Pending().Save(text);
        }
        catch (Exception writeFailure)
        {
            Log.Error(IndexIdentity.DisplayName, $"The fault could not be written to the device:\n{writeFailure}");
        }
        return text;
    }

    internal static string Record(string where, Exception exception) => Record(where, exception.ToString());

    /// <summary>The fault recorded by an earlier run that has not yet been shown, or null.</summary>
    internal static string? PendingReport() => Pending().ReadText();

    internal static void ClearPending() => Pending().Clear();
}
