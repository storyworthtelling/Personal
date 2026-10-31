using System.Globalization;

namespace Storyworthtelling.Diagnostics;

/// <summary>A stretch in which a worker should have run and did not.</summary>
public sealed record WorkerGap(DateTimeOffset From, DateTimeOffset To, int MissedRuns);

/// <summary>
/// G7 (LOG-LOOP-PLAN.md §4.3): a worker that silently never runs shows up only as a gap in its expected
/// cadence. A stretch counts as a gap when it is longer than the cadence plus a grace (by default one more
/// cadence), because WorkManager's periodic work drifts and its flex is not a failure.
/// </summary>
public static class WorkerCadence
{
    public static IReadOnlyList<WorkerGap> Missed(
        IReadOnlyList<DateTimeOffset> runs, TimeSpan expectedEvery, DateTimeOffset since, DateTimeOffset now, TimeSpan? grace = null)
    {
        var allowed = expectedEvery + (grace ?? expectedEvery);
        var points = new List<DateTimeOffset> { since };
        points.AddRange(runs.Where(r => r >= since && r <= now).OrderBy(r => r));
        points.Add(now);

        var gaps = new List<WorkerGap>();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var span = points[i + 1] - points[i];
            if (span > allowed)
            {
                var missed = (int)(span.Ticks / expectedEvery.Ticks) - (i + 1 < points.Count - 1 ? 1 : 0);
                gaps.Add(new WorkerGap(points[i], points[i + 1], Math.Max(1, missed)));
            }
        }
        return gaps;
    }
}

/// <summary>The worker trail's lines: start, end, result and duration of every worker run.</summary>
public static class WorkerTrail
{
    public static string StartedLine(string worker) => $"WORKER {worker} started";

    public static string FinishedLine(string worker, string result, TimeSpan duration) =>
        string.Create(CultureInfo.InvariantCulture, $"WORKER {worker} finished: {result} in {duration.TotalSeconds:0.00} s");

    public static string ThrewLine(string worker, TimeSpan duration, Exception exception) =>
        string.Create(CultureInfo.InvariantCulture, $"WORKER {worker} threw after {duration.TotalSeconds:0.00} s: {exception.GetType().FullName}");
}

/// <summary>Logged once per process start, so every later line can be tied to a build.</summary>
public static class LaunchLine
{
    public static string Format(string app, string version, long versionCode, string osRelease, int apiLevel) =>
        string.Create(CultureInfo.InvariantCulture, $"LAUNCH {app} {version} ({versionCode}) on Android {osRelease} (API {apiLevel})");
}
