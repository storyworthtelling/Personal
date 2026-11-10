using System.Text.RegularExpressions;
using Storyworthtelling.Diagnostics;

namespace LogLoopReader;

/// <summary>A worker that should have run on a phone and did not, over a stretch of the week's logs.</summary>
public sealed record MissedWorker(string App, string Phone, string Worker, int MissedRuns, DateTimeOffset From, DateTimeOffset To);

/// <summary>Runs of a worker that started and never said how they ended: killed at the cap, or the process died.</summary>
public sealed record UnfinishedRuns(string App, string Phone, string Worker, int Count);

/// <summary>A <c>DROPPED</c> line: segments the phone's outbox let go before they could be uploaded.</summary>
public sealed record DroppedSegments(string App, string Phone, string Line);

/// <summary>
/// What the week's logs say about the loop itself rather than about the apps (audit 08 §2, §5, §34): segments dropped on
/// the phone, an app a phone should have sent and did not, and a worker that stopped. Each is invisible as a group,
/// because what it means is that lines are missing.
/// </summary>
public sealed partial record LoopHealth(
    IReadOnlyList<DroppedSegments> Dropped,
    IReadOnlyList<(string App, string Phone)> Silent,
    IReadOnlyList<MissedWorker> MissedWorkers)
{
    /// <summary>Starts with no finish, threw or stopped line before the worker's next start or the end of the week.</summary>
    public IReadOnlyList<UnfinishedRuns> Unfinished { get; init; } = [];

    /// <summary>
    /// Which apps each phone runs. The locked FocusPhone heads and Folio run on the owner's phone only; Index,
    /// Constitution, Pulp and Foreword run on both. Roles, never device ids, so nothing here names a phone.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ExpectedApps = new Dictionary<string, IReadOnlyList<string>>
    {
        [LogPhone.Owner] = ["flyleaf", "score", "post", "plate", "pulp", "constitution", "index", "folio", "foreword"],
        [LogPhone.Partner] = ["pulp", "constitution", "index", "foreword"],
    };

    /// <summary>
    /// Each app's workers and how often each is scheduled, read from their <c>PeriodicWorkRequest</c>s (Foreword's
    /// delivery chains itself daily). A worker with a network constraint can slip while offline; the cadence check
    /// allows one extra period before it counts a gap.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Worker, TimeSpan Every)>> ExpectedWorkers =
        new Dictionary<string, IReadOnlyList<(string Worker, TimeSpan Every)>>
        {
            ["flyleaf"] = [("UpdateWorker", TimeSpan.FromHours(6)), ("LoopUploadWorker", TimeSpan.FromDays(1))],
            ["score"] =
            [
                ("ScoreUpdateWorker", TimeSpan.FromDays(1)), ("FridayPicksWorker", TimeSpan.FromDays(1)),
                ("MusicScanWorker", TimeSpan.FromDays(1)), ("LoopUploadWorker", TimeSpan.FromDays(1)),
            ],
            ["post"] = [("EmailSyncWorker", TimeSpan.FromMinutes(30)), ("LogUploadWorker", TimeSpan.FromDays(1))],
            ["plate"] = [("LoopUploadWorker", TimeSpan.FromDays(1))],
            ["pulp"] = [("CalendarSyncWorker", TimeSpan.FromDays(1)), ("LoopUploadWorker", TimeSpan.FromDays(1))],
            ["constitution"] =
            [
                ("BodySyncWorker", TimeSpan.FromHours(12)), ("ReadBackWorker", TimeSpan.FromDays(1)),
                ("WorkoutWeekWorker", TimeSpan.FromDays(1)), ("MealWeekWorker", TimeSpan.FromHours(1)),
                ("DigestWorker", TimeSpan.FromHours(1)), ("LoopUploadWorker", TimeSpan.FromDays(1)),
            ],
            ["index"] = [("RefreshWorker", TimeSpan.FromDays(1)), ("WorksheetDeliveryWorker", TimeSpan.FromDays(1))],
            ["folio"] = [("PipelineWorker", TimeSpan.FromMinutes(15))],
            ["foreword"] = [("DeliveryWorker", TimeSpan.FromDays(1))],
        };

    public static LoopHealth From(IReadOnlyList<LogBatch> batches)
    {
        var dropped = new List<DroppedSegments>();
        var runs = new Dictionary<(string App, string Phone, string Worker), List<DateTimeOffset>>();
        var spans = new Dictionary<(string App, string Phone), (DateTimeOffset First, DateTimeOffset Last)>();
        var open = new Dictionary<(string App, string Phone, string Worker), bool>();
        var unfinished = new Dictionary<(string App, string Phone, string Worker), int>();

        foreach (var batch in batches)
        {
            var header = batch.Header;
            var pair = (header.App, header.Phone);
            spans[pair] = spans.TryGetValue(pair, out var span)
                ? (Min(span.First, header.FirstUtc), Max(span.Last, header.LastUtc))
                : (header.FirstUtc, header.LastUtc);

            foreach (var part in batch.Parts)
            {
                foreach (var entry in LogEntries.Parse(part.Text))
                {
                    if (entry.Level == "DROPPED")
                    {
                        dropped.Add(new DroppedSegments(header.App, header.Phone, entry.Message));
                    }
                    else if (entry.Source == "Worker" && WorkerLine().Match(entry.Message) is { Success: true } line)
                    {
                        var key = (header.App, header.Phone, line.Groups["worker"].Value);
                        var started = line.Groups["what"].Value == "started";
                        if (started && EntryTime.Read(entry.Stamp, header) is { } when)
                        {
                            if (!runs.TryGetValue(key, out var list))
                            {
                                runs[key] = list = [];
                            }
                            list.Add(when);
                        }
                        // A start opens a run; a finish, throw, stop or skip closes it. A start while one is open
                        // means the open one never ended.
                        if (open.TryGetValue(key, out var isOpen) && isOpen && started)
                        {
                            unfinished[key] = unfinished.GetValueOrDefault(key) + 1;
                        }
                        open[key] = started;
                    }
                }
            }
        }

        var heard = spans.Keys.ToHashSet();
        var silent = ExpectedApps
            .SelectMany(phone => phone.Value.Select(app => (App: app, Phone: phone.Key)))
            .Where(pair => !heard.Contains(pair))
            .OrderBy(pair => pair.Phone, StringComparer.Ordinal).ThenBy(pair => pair.App, StringComparer.Ordinal)
            .ToList();

        // Only a phone that sent this app's logs can be judged: silence from an app is already "sent nothing".
        var missed = new List<MissedWorker>();
        foreach (var ((app, phone), (first, last)) in spans.OrderBy(s => s.Key.App, StringComparer.Ordinal).ThenBy(s => s.Key.Phone, StringComparer.Ordinal))
        {
            if (!ExpectedWorkers.TryGetValue(app, out var workers))
            {
                continue;
            }
            foreach (var (worker, every) in workers)
            {
                var seen = runs.TryGetValue((app, phone, worker), out var list) ? list : [];
                foreach (var gap in WorkerCadence.Missed(seen, every, first, last))
                {
                    missed.Add(new MissedWorker(app, phone, worker, gap.MissedRuns, gap.From, gap.To));
                }
            }
        }

        // A run still open at the end of the week's lines may simply not have finished before the upload, so it is not
        // counted; only a start followed by another start is.
        var cut = unfinished
            .OrderBy(u => u.Key.App, StringComparer.Ordinal).ThenBy(u => u.Key.Phone, StringComparer.Ordinal).ThenBy(u => u.Key.Worker, StringComparer.Ordinal)
            .Select(u => new UnfinishedRuns(u.Key.App, u.Key.Phone, u.Key.Worker, u.Value))
            .ToList();
        return new LoopHealth(dropped, silent, missed) { Unfinished = cut };
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    [GeneratedRegex(@"^WORKER (?<worker>\S+) (?<what>started|finished|threw|stopped|skipped)\b")]
    private static partial Regex WorkerLine();
}
