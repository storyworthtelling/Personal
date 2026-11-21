using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using Watch.Domain.Design;
using Watch.Domain.Kit;

namespace Watch.Ui.Wear;

/// <summary>
/// A progress ring round the face's edge (mock: Running to cadence, the rest timer): a faint track and,
/// from twelve o'clock clockwise, the filled part in an app's day colour. TalkBack reads it as a
/// progress bar with the host's words (<see cref="View.ContentDescription"/>).
/// </summary>
public class RingView : View
{
    private double fraction;

    public RingView(Context context)
        : base(context)
    {
    }

    public RingView(Context context, IAttributeSet? attrs)
        : base(context, attrs)
    {
    }

    protected RingView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>The app whose day colour fills the ring.</summary>
    public WatchApp App { get; set; } = WatchApp.Beat;

    /// <summary>How full, 0 to 1.</summary>
    public double Fraction
    {
        get => fraction;
        set
        {
            fraction = Math.Clamp(value, 0, 1);
            Invalidate();
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var radius = Math.Min(Width, Height) / 2f;
        if (radius <= 0)
        {
            return;
        }
        RingPainter.Draw(
            canvas,
            Width / 2f,
            Height / 2f,
            (float)(radius * GlyphGeometry.RingRadius),
            (float)(radius * GlyphGeometry.RingStroke),
            fraction,
            WatchPaints.Glass(App, WatchPaints.Now()));
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null)
        {
            return;
        }
        info.ClassName = "android.widget.ProgressBar";
        info.SetRangeInfo(new AccessibilityNodeInfo.RangeInfo(
            (int)RangeType.Percent, 0, 100, (float)Math.Round(fraction * 100)));
    }
}

/// <summary>A faint track and an arc from twelve o'clock, clockwise.</summary>
internal static class RingPainter
{
    public static void Draw(Canvas canvas, float centreX, float centreY, float radius, float stroke, double fraction, Oklch colour)
    {
        var bounds = new RectF(centreX - radius, centreY - radius, centreX + radius, centreY + radius);
        using (var track = WatchPaints.Stroke(WatchTokens.Neutral.Faint, stroke))
        {
            canvas.DrawOval(bounds, track);
        }
        if (fraction <= 0)
        {
            return;
        }
        using var arc = WatchPaints.Stroke(colour, stroke);
        canvas.DrawArc(bounds, -90, (float)(fraction * 360), false, arc);
    }
}
