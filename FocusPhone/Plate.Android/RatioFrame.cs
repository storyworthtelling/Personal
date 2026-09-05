using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Plate;

namespace Plate;

/// <summary>
/// The viewfinder's frame, measured to the ratio the sensor is capturing.
///
/// <para>This is the enforcement of <see cref="CaptureFormat"/>'s one rule. A plain
/// FrameLayout given the remaining height would stretch the preview or letterbox it inside
/// itself; this takes its width from the page and its height from the ratio, so switching
/// to 3:4 makes the window <b>shorter</b> and the empty space comes back rather than the
/// picture being cropped to fill it.</para>
/// </summary>
public sealed class RatioFrame(Context context) : FrameLayout(context)
{
    private WindowRatio ratio = CaptureFormat.Window(CaptureFormat.Default);

    public void SetRatio(CaptureRatio value)
    {
        ratio = CaptureFormat.Window(value);
        RequestLayout();
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        if (width <= 0)
        {
            base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
            return;
        }

        // Cap at the space actually available: on a short screen a 9:16 window would
        // otherwise push the controls off the bottom, and controls that cannot be reached
        // are worse than a window that is not quite full width.
        var height = width * ratio.Height / ratio.Width;
        var available = MeasureSpec.GetSize(heightMeasureSpec);
        if (MeasureSpec.GetMode(heightMeasureSpec) != MeasureSpecMode.Unspecified
            && available > 0 && height > available)
        {
            height = available;
            width = height * ratio.Width / ratio.Height;
        }

        base.OnMeasure(
            MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.Exactly),
            MeasureSpec.MakeMeasureSpec(height, MeasureSpecMode.Exactly));
    }
}
