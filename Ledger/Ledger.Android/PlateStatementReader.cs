using global::Android.App;
using global::Android.Content;
using Index.Domain.Handoff;
using Microsoft.Extensions.Logging;

namespace Index.Android;

/// <summary>
/// Starts Plate's statement read from a hand-entered account and waits for the answer (audit W2). The intent names
/// Plate's guarded reading screen outright, so nothing else can answer and nothing else can read a balance off it;
/// the answer comes back through <c>MainActivity.OnActivityResult</c>. The log says how a read ended, never the figure.
/// </summary>
public static class PlateStatementReader
{
    public const int RequestCode = 9102;

    private static TaskCompletionSource<(PlateReadOutcome Outcome, PlateStatementAnswer? Answer)>? pending;

    public static Task<(PlateReadOutcome Outcome, PlateStatementAnswer? Answer)> ReadAsync(Activity activity)
    {
        var intent = new global::Android.Content.Intent(PlateStatementRead.Action)
            .SetClassName(PlateStatementRead.PlatePackage, PlateStatementRead.Activity);

        pending?.TrySetResult((PlateReadOutcome.Cancelled, null));
        pending = new TaskCompletionSource<(PlateReadOutcome Outcome, PlateStatementAnswer? Answer)>();
        try
        {
            activity.StartActivityForResult(intent, RequestCode);
            Log("Statement read sent to Plate");
        }
        catch (ActivityNotFoundException)
        {
            Finish(PlateReadOutcome.NotInstalled, null);
        }
        catch (Java.Lang.SecurityException)
        {
            Finish(PlateReadOutcome.NotPermitted, null);
        }

        return pending?.Task ?? Task.FromResult<(PlateReadOutcome, PlateStatementAnswer?)>((PlateReadOutcome.Cancelled, null));
    }

    /// <summary>Called by MainActivity for every result; ignores any request that is not this one.</summary>
    public static void HandleResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != RequestCode)
        {
            return;
        }

        if (resultCode != Result.Ok || data is null)
        {
            Finish(PlateReadOutcome.Cancelled, null);
            return;
        }

        var answer = PlateStatementRead.Parse(
            data.GetStringExtra(PlateStatementRead.BalanceExtra), data.GetStringExtra(PlateStatementRead.AsOfExtra));
        Finish(answer is null ? PlateReadOutcome.Unreadable : PlateReadOutcome.Read, answer);
    }

    private static void Finish(PlateReadOutcome outcome, PlateStatementAnswer? answer)
    {
        Log($"Statement read ended: {outcome}");
        var waiting = pending;
        pending = null;
        waiting?.TrySetResult((outcome, answer));
    }

    private static void Log(string line) =>
        IPlatformApplication.Current?.Services.GetService<ILogger<PlateStatementReaderLog>>()?.LogInformation("{Line}", line);
}

/// <summary>The logger category for <see cref="PlateStatementReader"/>, which is static.</summary>
public sealed class PlateStatementReaderLog;
