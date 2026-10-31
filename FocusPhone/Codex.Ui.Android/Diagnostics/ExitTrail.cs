using Android.App;
using Android.Content;
using Storyworthtelling.Diagnostics;

namespace Codex.Ui;

/// <summary>
/// G6 (LOG-LOOP-PLAN.md §4.3): a process killed by Android, an ANR or a force-stop leaves nothing behind,
/// so each launch asks <c>ActivityManager.GetHistoricalProcessExitReasons</c> how the earlier ones ended and
/// logs every exit not logged before. This is the record that would have shown Post being force-stopped.
/// The phrasing is <see cref="ExitReasonPhrases"/> (pure, tested); the last exit logged is remembered in
/// the head's own preferences so a launch never repeats one.
/// </summary>
public static class ExitTrail
{
    private const string PreferencesName = "log-loop";
    private const string LastSeenKey = "exitLastSeen";

    /// <param name="log">Given whether the exit was a problem, and its line.</param>
    public static void Record(Context context, Action<bool, string> log)
    {
        try
        {
            if (context.GetSystemService(Context.ActivityService) is not ActivityManager manager || context.PackageName is null)
            {
                return;
            }
            var records = (manager.GetHistoricalProcessExitReasons(context.PackageName, 0, 0) ?? [])
                .Select(info => new ExitRecord(info.Reason, info.Status, info.Description, info.Timestamp, info.Importance))
                .ToList();

            var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
            var fresh = ExitReasonPhrases.NewSince(records, preferences.GetLong(LastSeenKey, 0));
            foreach (var record in fresh)
            {
                var when = DateTimeOffset.FromUnixTimeMilliseconds(record.Timestamp).ToLocalTime();
                log(ExitReasonPhrases.IsProblem(record.Reason), $"EXIT at {when:yyyy-MM-dd HH:mm:ss zzz} — {ExitReasonPhrases.Describe(record)}");
            }
            if (fresh.Count > 0)
            {
                preferences.Edit()!.PutLong(LastSeenKey, fresh[^1].Timestamp)!.Apply();
            }
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("ExitTrail", $"Could not read the exit reasons: {e.Message}");
        }
    }
}
