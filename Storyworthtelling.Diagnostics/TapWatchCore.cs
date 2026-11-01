
namespace Storyworthtelling.Diagnostics;

/// <summary>
/// A declared key action in flight (LOG-LOOP-PLAN.md §4.1, O10): the code that started background work
/// says how it ended. The first report wins; a late second one is ignored, so a success followed by a
/// stray failure callback cannot log a failure that did not happen.
/// </summary>
public sealed class KeyAction
{
    private readonly TapWatchCore? core;
    private int reported;

    internal KeyAction(TapWatchCore? core, string id)
    {
        this.core = core;
        Id = id;
    }

    public string Id { get; }

    /// <summary>A handle for when no watch is installed: reports go nowhere, the code reads the same.</summary>
    public static KeyAction Unwatched(string id) => new(null, id);

    public void Succeeded()
    {
        if (Interlocked.Exchange(ref reported, 1) == 0)
        {
            core?.ActionSucceeded(Id);
        }
    }

    public void Failed(string reason)
    {
        if (Interlocked.Exchange(ref reported, 1) == 0)
        {
            core?.ActionFailed(Id, reason);
        }
    }
}

/// <summary>
/// The Android-free heart of <c>Codex.Ui.TapWatch</c>: one <see cref="TapJudge"/> behind a lock, fed by
/// the window hook (touches, layout changes), by PaperUi's wrapped click handlers, and by key actions
/// that may finish on any thread; verdicts go to the head's log through <c>report</c>. The clock is
/// injected (the device passes its uptime clock), which is what makes this testable.
/// <para>
/// Diagnostics never break the app: a report that throws is dropped. A click handler that throws is
/// reported as <c>TAP THREW</c> and then rethrown, so the app behaves exactly as it did before the
/// watch — the crash handler still records the crash, now with the taps that led to it.
/// </para>
/// </summary>
public sealed class TapWatchCore
{
    /// <summary>A control's label is cut here, so tapping a message body does not make it a control name.</summary>
    public const int MaxLabelLength = 40;

    /// <summary>
    /// How a control is named in a tap line, by the click wrapper (label only) and the touch hook (label,
    /// view id, type) alike: its visible label, then <c>#id</c> when it has one, else its view type.
    /// </summary>
    public static string ControlName(string? text, string? idEntryName, string typeName)
    {
        var label = (text ?? string.Empty).Trim();
        if (label.Length > MaxLabelLength)
        {
            label = label[..MaxLabelLength] + "…";
        }
        var id = string.IsNullOrWhiteSpace(idEntryName) ? string.Empty : "#" + idEntryName;
        if (label.Length > 0)
        {
            return id.Length > 0 ? $"{label} {id}" : label;
        }
        return id.Length > 0 ? id : typeName;
    }

    /// <summary>How often the hook ticks while anything is pending.</summary>
    public const long TickAfterTapMs = 500;

    private readonly Func<long> clock;
    private readonly Action<TapVerdict> report;
    private readonly TapJudge judge = new();
    private readonly object gate = new();
    private string screen = "(none)";

    public TapWatchCore(Func<long> clock, Action<TapVerdict> report)
    {
        this.clock = clock;
        this.report = report;
    }

    public string Screen
    {
        get
        {
            lock (gate)
            {
                return screen;
            }
        }
    }

    public void ScreenShown(string name) => Feed(j =>
    {
        screen = name;
        return j.ScreenChanged(clock(), name);
    });

    public void Touched(string control, TapPoint point, bool enabled, bool isControl) =>
        Feed(j => j.Tap(clock(), screen, control, point, enabled, isControl));

    public void ContentChanged() => Feed(j => j.ContentChanged(clock(), screen));

    public void StatusChanged() => Feed(j => j.StatusChanged(clock(), screen));

    public void Tick() => Feed(j => j.Tick(clock()));

    /// <summary>Null when nothing is pending; otherwise how long until the hook should tick again.</summary>
    public long? NextTickDelayMs()
    {
        lock (gate)
        {
            return judge.HasPending ? TickAfterTapMs : null;
        }
    }

    /// <summary>Declares a key action; it is never judged dead, and must report within <paramref name="within"/>.</summary>
    public KeyAction Action(string actionId, TimeSpan within)
    {
        Feed(j => j.ActionStarted(clock(), actionId, within));
        return new KeyAction(this, actionId);
    }

    /// <summary>What PaperUi's buttons run instead of their bare onClick.</summary>
    public Action Wrap(string control, Action onClick) => () =>
    {
        var shownOn = Screen;
        try
        {
            onClick();
        }
        catch (Exception e)
        {
            Feed(j => j.Threw(clock(), shownOn, control, e.ToString()));
            throw;
        }
        finally
        {
            Feed(j => j.HandlerFinished(clock(), shownOn, control));
        }
    };

    internal void ActionSucceeded(string actionId) => Feed(j => j.ActionSucceeded(clock(), actionId));

    internal void ActionFailed(string actionId, string reason) => Feed(j => j.ActionFailed(clock(), actionId, reason));

    private void Feed(Func<TapJudge, IReadOnlyList<TapVerdict>> step)
    {
        IReadOnlyList<TapVerdict> verdicts;
        lock (gate)
        {
            verdicts = step(judge);
        }
        foreach (var verdict in verdicts)
        {
            try
            {
                report(verdict);
            }
            catch
            {
                // A full disk or a broken log must never turn a tap into a crash.
            }
        }
    }
}
