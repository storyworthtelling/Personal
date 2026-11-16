using Android.Content;
using Android.Graphics;
using Storyworthtelling.Materials.Android;
using Android.OS;
using Android.Views;
using AccessibilityNodeInfo = Android.Views.Accessibility.AccessibilityNodeInfo;
using EventTypes = Android.Views.Accessibility.EventTypes;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>
/// DESIGN.md §7's Dial: one value, set by dragging round a 270° track.
///
/// It exists because the alternative does not work where it is used. A slider across
/// a 254dp screen spends about four density-independent pixels per BPM, which is a
/// precision nobody has with sweaty hands halfway through a run; the same range round
/// a dial is nearly three times the travel, and the grip is a thumb turning rather
/// than a thumb aiming.
///
/// The maths is <see cref="DialGeometry"/> — where the knob lands, and what a touch
/// means — so this stays a painter and the arithmetic is tested without a device.
/// </summary>
public sealed class Dial : View
{
    private const int TrackDp = 10;
    private const int KnobDp = 14;

    private readonly DialGeometry geometry;
    private readonly Paint track = new() { AntiAlias = true };
    private readonly Paint filled = new() { AntiAlias = true };
    private readonly Paint knob = new() { AntiAlias = true };
    private readonly Paint reading = new() { AntiAlias = true };

    private int value;
    private int? turnedFrom;
    private readonly string unit;
    private readonly int step;

    /// <param name="unit">What the number counts, said after it ("120 beats per minute").</param>
    /// <param name="step">How far one TalkBack scroll or D-pad press turns it.</param>
    public Dial(Context context, DialGeometry geometry, int value, Color hue, string unit, int step = 1) : base(context)
    {
        this.geometry = geometry;
        this.unit = unit;
        this.step = step;

        // Reachable without a drag (§17, audit W9.3): focusable for a D-pad or switch, read as a slider with its range.
        Focusable = true;
        this.value = Math.Clamp(value, geometry.Minimum, geometry.Maximum);

        track.SetStyle(Paint.Style.Stroke);
        track.StrokeWidth = PaperTheme.Dp(context, TrackDp);
        track.StrokeCap = Paint.Cap.Round;
        track.Color = PaperTheme.PaperShade;

        // DESIGN.md §7 calls this a colored-pencil filled arc: the hue with the pencil's tooth (§2).
        MaterialPainters.Pencil(filled, hue, context.Resources!.DisplayMetrics!.Density);
        filled.SetStyle(Paint.Style.Stroke);
        filled.StrokeWidth = PaperTheme.Dp(context, TrackDp);
        filled.StrokeCap = Paint.Cap.Round;

        knob.SetStyle(Paint.Style.Fill);
        knob.Color = hue;

        // The one figure on the screen, in the mono face figures are set in.
        reading.Color = PaperTheme.Ink;
        reading.TextAlign = Paint.Align.Center;
        reading.TextSize = PaperTheme.Sp(context, DesignTokens.TypeSp.Display);
        reading.SetTypeface(PaperTheme.Mono(context));
    }

    /// <summary>Fires as the dial is turned, with the value it now reads.</summary>
    public event Action<int>? Turned;

    /// <summary>
    /// Fires once the finger lifts, if the value moved.
    ///
    /// The pair matters where turning the dial costs something. <see cref="Turned"/>
    /// is for what must keep up with the finger — the reading, a count — and this is
    /// for what must happen once: a drag across thirty BPM is one change of tempo,
    /// not thirty.
    /// </summary>
    public event Action<int>? Settled;

    public int Value
    {
        get => value;
        set
        {
            var wanted = Math.Clamp(value, geometry.Minimum, geometry.Maximum);
            if (wanted == this.value)
            {
                return;
            }

            this.value = wanted;
            Invalidate();
        }
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null)
        {
            return;
        }

        info.ClassName = "android.widget.SeekBar";
        info.SetRangeInfo(new AccessibilityNodeInfo.RangeInfo(
            (int)Android.Views.Accessibility.RangeType.Int, geometry.Minimum, geometry.Maximum, value));
        info.StateDescription = $"{value} {unit}";
        if (value < geometry.Maximum)
        {
            info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionScrollForward);
        }
        if (value > geometry.Minimum)
        {
            info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionScrollBackward);
        }
    }

    public override bool PerformAccessibilityAction(global::Android.Views.Accessibility.Action action, Bundle? arguments) => action switch
    {
        global::Android.Views.Accessibility.Action.ScrollForward => Nudge(1),
        global::Android.Views.Accessibility.Action.ScrollBackward => Nudge(-1),
        _ => base.PerformAccessibilityAction(action, arguments),
    };

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e) => keyCode switch
    {
        Keycode.DpadRight or Keycode.DpadUp => Nudge(1),
        Keycode.DpadLeft or Keycode.DpadDown => Nudge(-1),
        _ => base.OnKeyDown(keyCode, e),
    };

    // One step, as a turn and a settle together: the same two events a finger's drag ends with.
    private bool Nudge(int direction)
    {
        var turned = geometry.Nudge(value, direction, step);
        if (turned == value)
        {
            return false;
        }

        value = turned;
        Invalidate();
        Turned?.Invoke(value);
        Settled?.Invoke(value);
        SendAccessibilityEvent(EventTypes.ViewSelected);
        return true;
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return false;
        }

        if (e.Action is MotionEventActions.Down or MotionEventActions.Move)
        {
            turnedFrom ??= value;

            // The parent is usually a scrolling column, and a drag round a dial reads
            // to it as a drag down the page — so the dial claims the gesture.
            Parent?.RequestDisallowInterceptTouchEvent(true);

            var turned = geometry.ValueAtPoint(e.GetX() - (Width / 2f), e.GetY() - (Height / 2f));
            if (turned != value)
            {
                value = turned;
                Invalidate();
                Turned?.Invoke(value);
            }

            return true;
        }

        if (e.Action is MotionEventActions.Up or MotionEventActions.Cancel)
        {
            Parent?.RequestDisallowInterceptTouchEvent(false);
            var from = turnedFrom;
            turnedFrom = null;
            if (from is not null && from != value)
            {
                Settled?.Invoke(value);
            }

            return true;
        }

        return base.OnTouchEvent(e);
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);

        var inset = (track.StrokeWidth / 2f) + PaperTheme.Dp(Context!, KnobDp) / 2f;
        var box = new RectF(inset, inset, Width - inset, Height - inset);

        canvas.DrawArc(box, (float)DialGeometry.StartDegrees, (float)DialGeometry.SweepDegrees, false, track);
        var sweep = (float)(DialGeometry.SweepDegrees * geometry.FractionOf(value));
        canvas.DrawArc(box, (float)DialGeometry.StartDegrees, sweep, false, filled);

        var radius = (box.Width() / 2f);
        var (x, y) = geometry.KnobAt(value, box.CenterX(), box.CenterY(), radius);
        canvas.DrawCircle((float)x, (float)y, PaperTheme.Dp(Context!, KnobDp) / 2f, knob);

        // Centred on the text's own middle, not the baseline, or the number sits low
        // in a ring that is exactly symmetrical.
        var metrics = reading.GetFontMetrics()!;
        var baseline = box.CenterY() - ((metrics.Ascent + metrics.Descent) / 2f);
        canvas.DrawText(value.ToString(), box.CenterX(), baseline, reading);
    }
}
