using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using Watch.Domain.Design;
using Watch.Domain.Kit;

namespace Watch.Ui.Wear;

/// <summary>
/// Wave's player (A72; mock: Now playing, Paused, Turning the crown, Next track): the sine across the
/// face, bright as far as the track has played, its height the volume, flat when paused, with the back
/// and skip marks at its edges. Tap the middle to pause or play, an edge to go back or skip; the crown
/// changes the height. A thin painter over <see cref="WaveGeometry"/>.
/// </summary>
public class WaveView : View
{
    private double progress;
    private double amplitude = WaveGeometry.AmplitudeFor(0.5);
    private bool paused;
    private WaveTarget? pressed;

    public WaveView(Context context)
        : base(context)
    {
        Setup();
    }

    public WaveView(Context context, IAttributeSet? attrs)
        : base(context, attrs)
    {
        Setup();
    }

    protected WaveView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Raised for a tap (or TalkBack's action) on the middle, the left edge or the right edge.</summary>
    public event EventHandler<WaveTarget>? Tapped;

    /// <summary>Raised for each crown detent: positive is louder. <see cref="Amplitude"/> has already moved.</summary>
    public event EventHandler<int>? CrownTurned;

    /// <summary>How far through the track, 0 to 1: the bright part.</summary>
    public double Progress
    {
        get => progress;
        set
        {
            progress = Math.Clamp(value, 0, 1);
            Invalidate();
        }
    }

    /// <summary>The wave's height as a fraction of the face's radius (see <see cref="WaveGeometry.AmplitudeFor"/>).</summary>
    public double Amplitude
    {
        get => amplitude;
        set
        {
            amplitude = Math.Clamp(value, WaveGeometry.Quietest, WaveGeometry.Loudest);
            Invalidate();
        }
    }

    /// <summary>Paused lies flat.</summary>
    public bool Paused
    {
        get => paused;
        set
        {
            paused = value;
            ContentDescription = paused ? KitWords.Paused : KitWords.Playing;
            Invalidate();
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var radius = Width / 2.0;
        if (radius <= 0)
        {
            return;
        }

        var frame = new WaveFrame(WaveGeometry.LeftOn(radius), radius * WaveGeometry.Span, Height / 2.0);
        var height = amplitude * radius;
        var colour = WatchPaints.Glass(WatchApp.Wave, WatchPaints.Now());
        var stroke = (float)(radius * WaveGeometry.Stroke);

        using (var all = WatchPaints.Line(WaveGeometry.Points(frame, height, paused)))
        using (var faint = WatchPaints.Stroke(colour, stroke, WatchTokens.Light.Unplayed))
        {
            if (all is not null)
            {
                canvas.DrawPath(all, faint);
            }
        }

        using (var bright = WatchPaints.Line(WaveGeometry.Bright(frame, height, paused, progress)))
        using (var lit = WatchPaints.Stroke(colour, stroke, 1, (float)(radius * WaveGeometry.Glow)))
        {
            if (bright is not null)
            {
                canvas.DrawPath(bright, lit);
            }
        }

        DrawMark(canvas, radius, (float)frame.Baseline, WaveGeometry.BackMark, pressed == WaveTarget.Back, colour);
        DrawMark(canvas, radius, (float)frame.Baseline, WaveGeometry.SkipMark, pressed == WaveTarget.Skip, colour);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return base.OnTouchEvent(e);
        }

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                pressed = WaveGeometry.TargetAt(e.GetX(), Width, WatchPaints.TouchTarget(Context!));
                Invalidate();
                return true;
            case MotionEventActions.Up:
                var target = pressed ?? WaveTarget.PlayPause;
                pressed = null;
                Invalidate();
                if (target == WaveTarget.PlayPause)
                {
                    return PerformClick();
                }
                Tapped?.Invoke(this, target);
                return true;
            case MotionEventActions.Cancel:
                pressed = null;
                Invalidate();
                return true;
            default:
                return true;
        }
    }

    /// <summary>A tap on the middle, from a finger or from TalkBack: pause or play.</summary>
    public override bool PerformClick()
    {
        base.PerformClick();
        Tapped?.Invoke(this, WaveTarget.PlayPause);
        return true;
    }

    /// <summary>The crown (rotary input) makes the wave taller or shallower, one step a detent.</summary>
    public override bool OnGenericMotionEvent(MotionEvent? e)
    {
        if (e is null || e.Action != MotionEventActions.Scroll || !e.IsFromSource(InputSourceType.RotaryEncoder))
        {
            return base.OnGenericMotionEvent(e);
        }

        var detents = -Math.Sign(e.GetAxisValue(Axis.Scroll));
        if (detents == 0)
        {
            return true;
        }
        Amplitude = WaveGeometry.Nudge(amplitude, detents);
        CrownTurned?.Invoke(this, detents);
        return true;
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null)
        {
            return;
        }

        info.ClassName = "android.widget.Button";
        info.AddAction(new AccessibilityNodeInfo.AccessibilityAction(Resource.Id.waveBack, KitWords.Back));
        info.AddAction(new AccessibilityNodeInfo.AccessibilityAction(Resource.Id.waveSkip, KitWords.Skip));
    }

    public override bool PerformAccessibilityAction(global::Android.Views.Accessibility.Action action, Bundle? arguments)
    {
        if ((int)action == Resource.Id.waveBack)
        {
            Tapped?.Invoke(this, WaveTarget.Back);
            return true;
        }
        if ((int)action == Resource.Id.waveSkip)
        {
            Tapped?.Invoke(this, WaveTarget.Skip);
            return true;
        }
        return base.PerformAccessibilityAction(action, arguments);
    }

    private void Setup()
    {
        Clickable = true;
        Focusable = true;
        FocusableInTouchMode = true;
        ContentDescription = KitWords.Playing;
    }

    private static void DrawMark(Canvas canvas, double radius, float y, (double From, double To) mark, bool lit, Oklch glass)
    {
        var colour = lit ? glass : WatchTokens.Neutral.Soft;
        var alpha = lit ? 1 : WatchTokens.Light.EdgeMark;
        using var paint = WatchPaints.Stroke(colour, (float)(radius * WaveGeometry.MarkStroke), alpha);
        canvas.DrawLine((float)(radius * mark.From), y, (float)(radius * mark.To), y, paint);
    }
}
