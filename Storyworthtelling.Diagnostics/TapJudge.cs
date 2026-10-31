using System.Globalization;
using System.Text;

namespace Storyworthtelling.Diagnostics;

/// <summary>Where a finger landed, in the window's pixels.</summary>
public readonly record struct TapPoint(float X, float Y)
{
    public float DistanceTo(TapPoint other) =>
        MathF.Sqrt(((X - other.X) * (X - other.X)) + ((Y - other.Y) * (Y - other.Y)));
}

/// <summary>One tap in the ring: never written on its own, only as context to a problem (O8).</summary>
public sealed record TapRecord(long AtMs, string Screen, string Control);

public enum TapVerdictKind
{
    Dead,
    Repeated,
    RepeatedOnDisabled,
    NonControl,
    Threw,
    ActionFailed,
    ActionNoResult,
}

/// <summary>A problem the judge found, with the last taps before it as context.</summary>
public sealed record TapVerdict(
    TapVerdictKind Kind,
    long AtMs,
    string Screen,
    string Control,
    int Count,
    string? Detail,
    IReadOnlyList<TapRecord> Recent)
{
    /// <summary>The fixed label a log line starts with — what the reader's digest groups on.</summary>
    public string Label => Kind switch
    {
        TapVerdictKind.Dead => "DEAD TAP",
        TapVerdictKind.Repeated => string.Create(CultureInfo.InvariantCulture, $"REPEATED TAP ×{Count}"),
        TapVerdictKind.RepeatedOnDisabled => string.Create(CultureInfo.InvariantCulture, $"REPEATED TAP ×{Count} ON DISABLED"),
        TapVerdictKind.NonControl => "TAPS ON NON-CONTROL",
        TapVerdictKind.Threw => "TAP THREW",
        TapVerdictKind.ActionFailed => "ACTION FAILED",
        _ => "ACTION NO RESULT",
    };

    public string ToLogText()
    {
        var builder = new StringBuilder();
        builder.Append(Label).Append(" — screen ").Append(Screen).Append(" · control ").Append(Control);
        if (Kind == TapVerdictKind.NonControl)
        {
            builder.Append(" ×").Append(Count.ToString(CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrEmpty(Detail))
        {
            builder.Append('\n').Append(Detail);
        }
        if (Recent.Count > 0)
        {
            builder.Append("\nrecent taps (oldest first):");
            foreach (var tap in Recent)
            {
                builder.Append("\n  ").Append(tap.Screen).Append(" · ").Append(tap.Control).Append(" · ")
                    .Append((AtMs - tap.AtMs).ToString(CultureInfo.InvariantCulture)).Append(" ms before");
            }
        }
        return builder.ToString();
    }
}

/// <summary>
/// The pure judge behind the tap watch (LOG-LOOP-PLAN.md §4.1). The kit hooks feed it timestamped events
/// on one monotonic clock; it returns verdicts. Every call first settles what has matured by its own time,
/// so it needs no timer of its own — the hook ticks it.
/// <list type="bullet">
/// <item><b>Dead</b>: an enabled control, no screen, content or status change within
/// <see cref="DeadWindow"/>, and no declared action running. Within one burst only the first dead tap is
/// reported; the burst's count says the rest.</item>
/// <item><b>Repeated</b>: <see cref="RepeatCount"/>+ taps on one control, or within
/// <see cref="RepeatRadius"/> px of one spot, inside <see cref="RepeatWindow"/>, whatever happened;
/// reported once when the burst ends, with its count. On a disabled control this is the only report;
/// on something that is not a control it reads as taps on a non-control.</item>
/// <item><b>Threw</b>, <b>action failed</b> and <b>no result</b> are reported as they happen.</item>
/// </list>
/// A declared action (<see cref="ActionStarted"/>) is never judged dead: it removes the main false
/// positive, a button that works silently in the background.
/// </summary>
public sealed class TapJudge
{
    public static readonly TimeSpan DeadWindow = TimeSpan.FromMilliseconds(1_500);
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(4);
    public const int RepeatCount = 3;
    public const float RepeatRadius = 48f;
    public const int RingSize = 20;

    private static readonly long DeadMs = (long)DeadWindow.TotalMilliseconds;
    private static readonly long RepeatMs = (long)RepeatWindow.TotalMilliseconds;

    private readonly List<TapRecord> ring = [];
    private readonly List<PendingDead> pendingDead = [];
    private readonly List<Burst> bursts = [];
    private readonly Dictionary<string, RunningAction> actions = new(StringComparer.Ordinal);
    private long lastActionStartedMs = long.MinValue;
    private string lastScreen = string.Empty;

    public IReadOnlyList<TapRecord> Recent => ring.ToList();

    public IReadOnlyList<TapVerdict> Tap(long atMs, string screen, string control, TapPoint point, bool enabled, bool isControl)
    {
        var verdicts = Settle(atMs);
        lastScreen = screen;

        ring.Add(new TapRecord(atMs, screen, control));
        if (ring.Count > RingSize)
        {
            ring.RemoveAt(0);
        }

        var burst = bursts.FirstOrDefault(b => b.Matches(screen, control, point, atMs));
        var firstOfBurst = burst is null;
        if (burst is null)
        {
            burst = new Burst(screen, control, point);
            bursts.Add(burst);
        }
        burst.Add(atMs, enabled, isControl);

        var actionRunning = actions.Values.Any(a => a.DeadlineMs > atMs);
        if (enabled && isControl && !actionRunning && (firstOfBurst || !burst.HasDeadCandidate))
        {
            pendingDead.Add(new PendingDead(atMs, screen, control, burst));
            burst.HasDeadCandidate = true;
        }
        return verdicts;
    }

    public IReadOnlyList<TapVerdict> ScreenChanged(long atMs, string screen)
    {
        var verdicts = Settle(atMs);
        lastScreen = screen;
        pendingDead.Clear();
        return verdicts;
    }

    public IReadOnlyList<TapVerdict> ContentChanged(long atMs, string screen) => SomethingChanged(atMs);

    public IReadOnlyList<TapVerdict> StatusChanged(long atMs, string screen) => SomethingChanged(atMs);

    public IReadOnlyList<TapVerdict> ActionStarted(long atMs, string actionId, TimeSpan within)
    {
        var verdicts = Settle(atMs);
        actions[actionId] = new RunningAction(atMs, atMs + (long)within.TotalMilliseconds, lastScreen);
        lastActionStartedMs = atMs;
        pendingDead.Clear();
        return verdicts;
    }

    public IReadOnlyList<TapVerdict> ActionSucceeded(long atMs, string actionId)
    {
        var verdicts = Settle(atMs);
        actions.Remove(actionId);
        return verdicts;
    }

    public IReadOnlyList<TapVerdict> ActionFailed(long atMs, string actionId, string reason)
    {
        var verdicts = Settle(atMs);
        var screen = actions.TryGetValue(actionId, out var running) ? running.Screen : lastScreen;
        actions.Remove(actionId);
        verdicts.Add(Verdict(TapVerdictKind.ActionFailed, atMs, screen, actionId, 1, reason));
        return verdicts;
    }

    public IReadOnlyList<TapVerdict> Threw(long atMs, string screen, string control, string exception)
    {
        var verdicts = Settle(atMs);
        pendingDead.RemoveAll(p => p.Screen == screen && p.Control == control);
        verdicts.Add(Verdict(TapVerdictKind.Threw, atMs, screen, control, 1, exception));
        return verdicts;
    }

    /// <summary>Settles everything that has matured by <paramref name="atMs"/>.</summary>
    public IReadOnlyList<TapVerdict> Tick(long atMs) => Settle(atMs);

    private List<TapVerdict> SomethingChanged(long atMs)
    {
        var verdicts = Settle(atMs);
        pendingDead.Clear();
        return verdicts;
    }

    // Matured items in time order, so a verdict's ring shows exactly what came before it.
    private List<TapVerdict> Settle(long nowMs)
    {
        var matured = new List<(long At, Func<TapVerdict?> Make)>();

        foreach (var dead in pendingDead.Where(p => p.AtMs + DeadMs <= nowMs).ToList())
        {
            pendingDead.Remove(dead);
            var startedInWindow = lastActionStartedMs >= dead.AtMs && lastActionStartedMs < dead.AtMs + DeadMs;
            if (!startedInWindow)
            {
                matured.Add((dead.AtMs + DeadMs, () => Verdict(TapVerdictKind.Dead, dead.AtMs + DeadMs, dead.Screen, dead.Control, 1, null)));
            }
        }

        foreach (var burst in bursts.Where(b => b.LastMs + RepeatMs <= nowMs).ToList())
        {
            bursts.Remove(burst);
            if (burst.Qualifies)
            {
                var at = burst.LastMs + RepeatMs;
                matured.Add((at, () => Verdict(burst.Kind, at, burst.Screen, burst.Control, burst.Count, null)));
            }
        }

        foreach (var (id, action) in actions.Where(a => a.Value.DeadlineMs <= nowMs).ToList())
        {
            actions.Remove(id);
            matured.Add((action.DeadlineMs, () => Verdict(TapVerdictKind.ActionNoResult, action.DeadlineMs, action.Screen, id, 1,
                string.Create(CultureInfo.InvariantCulture, $"no result within {(action.DeadlineMs - action.StartedMs) / 1000.0:0.#} s"))));
        }

        return matured.OrderBy(m => m.At).Select(m => m.Make()).OfType<TapVerdict>().ToList();
    }

    private TapVerdict Verdict(TapVerdictKind kind, long atMs, string screen, string control, int count, string? detail) =>
        new(kind, atMs, screen, control, count, detail, ring.Where(t => t.AtMs <= atMs).ToList());

    private sealed record PendingDead(long AtMs, string Screen, string Control, Burst Burst);

    private sealed record RunningAction(long StartedMs, long DeadlineMs, string Screen);

    private sealed class Burst(string screen, string control, TapPoint origin)
    {
        private readonly List<long> times = [];
        private int disabled;
        private int nonControl;

        public string Screen { get; } = screen;
        public string Control { get; } = control;
        public bool HasDeadCandidate { get; set; }
        public long LastMs => times[^1];
        public int Count => times.Count;

        public bool Qualifies
        {
            get
            {
                for (var i = 0; i + RepeatCount - 1 < times.Count; i++)
                {
                    if (times[i + RepeatCount - 1] - times[i] <= RepeatMs)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public TapVerdictKind Kind =>
            nonControl == Count ? TapVerdictKind.NonControl
            : disabled == Count ? TapVerdictKind.RepeatedOnDisabled
            : TapVerdictKind.Repeated;

        public bool Matches(string screen, string control, TapPoint point, long atMs) =>
            atMs - LastMs <= RepeatMs
            && screen == Screen
            && (control == Control || point.DistanceTo(origin) <= RepeatRadius);

        public void Add(long atMs, bool enabled, bool isControl)
        {
            times.Add(atMs);
            if (!isControl)
            {
                nonControl++;
            }
            else if (!enabled)
            {
                disabled++;
            }
        }
    }
}
