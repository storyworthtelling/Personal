using Android.App.Usage;
using Android.Content;

namespace Flyleaf;

/// <summary>
/// Reads per-app foreground time over a recent window so the tray can size book spines by how much
/// the owner actually uses each app. Needs the special "usage access" permission (the owner grants
/// it in system settings); when it isn't granted the query returns an empty map, and
/// <see cref="Codex.Domain.Apps.SpineSizing"/> then makes every spine uniform — a graceful default.
/// </summary>
public static class UsageStatsReader
{
    /// <summary>Foreground milliseconds per package over roughly the last <paramref name="days"/> days.</summary>
    public static IReadOnlyDictionary<string, long> ForegroundTime(Context context, int days = 30)
    {
        var result = new Dictionary<string, long>();
        try
        {
            if (context.GetSystemService(Context.UsageStatsService) is not UsageStatsManager manager)
            {
                return result;
            }
            var end = Java.Lang.JavaSystem.CurrentTimeMillis();
            var begin = end - (long)days * 24 * 60 * 60 * 1000;
            var stats = manager.QueryUsageStats(UsageStatsInterval.Best, begin, end);
            if (stats is null)
            {
                return result;
            }
            foreach (var stat in stats)
            {
                if (stat.PackageName is null)
                {
                    continue;
                }
                result.TryGetValue(stat.PackageName, out var current);
                result[stat.PackageName] = current + stat.TotalTimeInForeground;
            }
        }
        catch (Java.Lang.Exception)
        {
            // Permission not granted or the service is unavailable — degrade to uniform spines.
        }
        return result;
    }
}
