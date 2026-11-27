using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using Action = System.Action;
using Watch.Domain.Design;
using Watch.Domain.Kit;

namespace Watch.Ui.Wear;

/// <summary>
/// The watch's undo (WD §9, O5; mock: Filed, with Undo): the app's glyph with a thin ring that empties
/// as the window runs out. The window is six seconds, or Android's "Time to take action" when longer
/// (<see cref="UndoWindow"/>). A tap anywhere takes the act back; when the ring empties the act is final.
/// The words ("Lee filed", "Tap to undo") are the host's, in <see cref="WatchTextView"/>s beside it. A host that tells
/// another device the window (Dash's act, P14 B5) asks for it first (<see cref="WindowFor"/>) and opens the ring for
/// exactly that, and may place the glyph above the middle (<see cref="GlyphCentre"/>, mock: at 0.62 of the radius).
/// </summary>
public class UndoRingView : View
{
    private long openedAt;
    private TimeSpan window;
    private Action? undo;
    private Action? final;

    public UndoRingView(Context context)
        : base(context)
    {
        Setup();
    }

    public UndoRingView(Context context, IAttributeSet? attrs)
        : base(context, attrs)
    {
        Setup();
    }

    protected UndoRingView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Whether the act can still be taken back.</summary>
    public bool IsOpen => undo is not null && UndoWindow.IsOpen(Elapsed, window);

    /// <summary>Where the glyph's centre sits, as a fraction of the view's half-height from its top: 1, the default, is the middle.</summary>
    public double GlyphCentre { get; set; } = 1;

    /// <summary>The window on this watch: six seconds, or Android's "Time to take action" when longer.</summary>
    public static TimeSpan WindowFor(Context context) => UndoWindow.For(Recommended(context));

    private TimeSpan Elapsed => TimeSpan.FromMilliseconds(SystemClock.UptimeMillis() - openedAt);

    /// <summary>
    /// Opens the window: <paramref name="onUndo"/> runs if the person taps in time, otherwise
    /// <paramref name="onFinal"/> runs once as the ring empties. Exactly one of them runs.
    /// </summary>
    public void Open(Action onUndo, Action onFinal) => Open(WindowFor(Context!), onUndo, onFinal);

    /// <summary>Opens the window for exactly <paramref name="window"/>, as <see cref="Open(Action, Action)"/> does.</summary>
    public void Open(TimeSpan window, Action onUndo, Action onFinal)
    {
        Close();
        undo = onUndo;
        final = onFinal;
        this.window = window;
        openedAt = SystemClock.UptimeMillis();
        var opened = openedAt;
        // The act becomes final when the window closes even if nothing is drawn (the screen asleep).
        PostDelayed(() => Expire(opened), (long)Math.Ceiling(window.TotalMilliseconds));
        PostInvalidateOnAnimation();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (undo is null)
        {
            return;
        }
        if (!UndoWindow.IsOpen(Elapsed, window))
        {
            Finish();
            return;
        }

        var radius = Math.Min(Width, Height) / 2f;
        var side = (float)(radius * GlyphGeometry.UndoGlyph);
        var colour = WatchPaints.Glass(WatchApp.Dash, WatchPaints.Now());
        var centreY = (float)(Height / 2f * GlyphCentre);
        GlyphPainter.Dash(canvas, Width / 2f, centreY, side, colour, 1);
        RingPainter.Draw(
            canvas,
            Width / 2f,
            centreY,
            (float)(side * GlyphGeometry.UndoRingRadius),
            (float)(radius * GlyphGeometry.UndoRingStroke),
            UndoWindow.Remaining(Elapsed, window),
            colour);
        PostInvalidateOnAnimation();
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e?.ActionMasked == MotionEventActions.Up)
        {
            return PerformClick();
        }
        return true;
    }

    /// <summary>A tap, from a finger or TalkBack: take the act back while the window is open.</summary>
    public override bool PerformClick()
    {
        base.PerformClick();
        if (!IsOpen)
        {
            return false;
        }
        var taken = undo!;
        Close();
        taken();
        return true;
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is not null)
        {
            info.ClassName = "android.widget.Button";
        }
    }

    protected override void OnDetachedFromWindow()
    {
        // Leaving the screen does not cancel the act: the window closing is what makes it final.
        if (undo is not null)
        {
            Finish();
        }
        base.OnDetachedFromWindow();
    }

    private void Setup()
    {
        Clickable = true;
        ContentDescription = KitWords.Undo;
    }

    private void Expire(long opened)
    {
        if (undo is not null && openedAt == opened && !UndoWindow.IsOpen(Elapsed, window))
        {
            Finish();
        }
    }

    private void Finish()
    {
        var done = final;
        Close();
        done?.Invoke();
    }

    private void Close()
    {
        undo = null;
        final = null;
        Invalidate();
    }

    private static TimeSpan Recommended(Context context)
    {
        if (context.GetSystemService(Context.AccessibilityService) is AccessibilityManager manager)
        {
            return TimeSpan.FromMilliseconds(manager.GetRecommendedTimeoutMillis(
                Storyworthtelling.Folds.FoldTiming.UndoWindowMs,
                ContentMode.Controls | ContentMode.Text));
        }
        return TimeSpan.Zero;
    }
}
