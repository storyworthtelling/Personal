using Android.Content;
using Android.Graphics;
using Android.Views;
using Codex.Domain.Home;

namespace Codex.Ui;

/// <summary>
/// The drawn analog clock for the launcher home (DESIGN.md §11): ink face
/// ring, ink minute hand, terracotta hour hand — the launcher icon come to
/// life. The hub dot doubles as the quiet mark for the long-press admin
/// gesture. A thin drawing shell over ClockAngles.
/// </summary>
public class PaperClockView : View
{
    private readonly Paint ring;
    private readonly Paint minuteHand;
    private readonly Paint hourHand;
    private readonly Paint hub;
    private TimeSpan timeOfDay;

    public PaperClockView(Context context) : base(context)
    {
        var stroke = PaperTheme.Dp(context, 2);
        ring = new Paint { AntiAlias = true, StrokeWidth = stroke, Color = PaperTheme.Ink };
        ring.SetStyle(Paint.Style.Stroke);
        minuteHand = new Paint { AntiAlias = true, StrokeWidth = stroke, Color = PaperTheme.Ink, StrokeCap = Paint.Cap.Round };
        hourHand = new Paint { AntiAlias = true, StrokeWidth = stroke * 1.5f, Color = PaperTheme.Accent, StrokeCap = Paint.Cap.Round };
        hub = new Paint { AntiAlias = true, Color = PaperTheme.Ink };
    }

    public void SetTime(TimeSpan value)
    {
        timeOfDay = value;
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var centerX = Width / 2f;
        var centerY = Height / 2f;
        var radius = Math.Min(centerX, centerY) - ring.StrokeWidth;

        canvas.DrawCircle(centerX, centerY, radius, ring);

        var (hourDegrees, minuteDegrees) = ClockAngles.FromTime(timeOfDay);
        DrawHand(canvas, centerX, centerY, hourDegrees, radius * 0.52f, hourHand);
        DrawHand(canvas, centerX, centerY, minuteDegrees, radius * 0.74f, minuteHand);

        canvas.DrawCircle(centerX, centerY, ring.StrokeWidth * 1.2f, hub);
    }

    private static void DrawHand(Canvas canvas, float centerX, float centerY, float degrees, float length, Paint paint)
    {
        var radians = (degrees - 90f) * Math.PI / 180.0;
        canvas.DrawLine(centerX, centerY,
            centerX + (float)Math.Cos(radians) * length,
            centerY + (float)Math.Sin(radians) * length,
            paint);
    }
}
