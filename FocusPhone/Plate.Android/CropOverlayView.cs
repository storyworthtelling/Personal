using Android.Content;
using Android.Graphics;
using Android.Views;
using Codex.Domain.Plate;
using Codex.Ui;

namespace Plate;

/// <summary>
/// The interactive half of Free crop: a dimmed scrim outside the current rectangle, a clear
/// window inside it, and a handle at each corner to drag. Sits over <c>window</c> (the
/// picture's own ImageView) at the same bounds, so it has to work out for itself where
/// FitCenter actually drew the picture — <see cref="DisplayRect"/> is that arithmetic,
/// mirroring what the ImageView does internally so a dragged handle lands on the picture
/// rather than on the letterboxed strip beside it.
///
/// <para>The touch maths itself — which corner a touch grabs, and where a drag moves it — is
/// <see cref="CropGesture"/>, pure and unit-tested; this view only converts screen pixels into
/// the picture's own pixel space and back. Validity is decided in exactly one place:
/// <see cref="PhotoEdit.CropTo"/>, which this view calls on every move rather than checking
/// the box itself, so a drag that crosses over itself simply stops rather than inverting.</para>
///
/// <para>Only the scrim redraws while a finger is down — cheap Canvas work, no bitmap
/// touched — so the rectangle feels live without re-cropping the full-resolution picture on
/// every move. The real pixels are only re-cropped once, when the finger lifts.</para>
/// </summary>
public sealed class CropOverlayView : View
{
    private const int HandleTouchRadiusDp = 32;
    private const int HandleDrawDp = 8;

    private readonly int imageWidth;
    private readonly int imageHeight;
    private readonly Action<PhotoEdit> onReleased;

    private readonly Paint scrim = new() { AntiAlias = true };
    private readonly Paint frame = new() { AntiAlias = true };
    private readonly Paint handle = new() { AntiAlias = true };

    private PhotoEdit workingEdit = PhotoEdit.None;
    private CropHandle? dragging;

    public CropOverlayView(Context context, int imageWidth, int imageHeight, Action<PhotoEdit> onReleased)
        : base(context)
    {
        this.imageWidth = imageWidth;
        this.imageHeight = imageHeight;
        this.onReleased = onReleased;

        scrim.Color = new Color(PaperTheme.Ink.R, PaperTheme.Ink.G, PaperTheme.Ink.B, (byte)140);

        frame.SetStyle(Paint.Style.Stroke);
        frame.StrokeWidth = PaperTheme.Dp(context, 2);
        frame.Color = PaperTheme.PlateMark;

        handle.SetStyle(Paint.Style.Fill);
        handle.Color = PaperTheme.PlateMark;
    }

    /// <summary>
    /// Starts (or restarts) a drag from the given edit's own crop box — called when Free
    /// crop is switched on, and whenever something else (Square, Portrait, Turn) changed the
    /// crop while the handles were hidden, so a drag never resumes from a stale rectangle.
    /// </summary>
    public void Sync(PhotoEdit current)
    {
        workingEdit = current;
        Invalidate();
    }

    // Where FitCenter actually draws the picture inside this view's bounds — letterboxed to
    // whichever dimension is tighter, then centred in the other.
    private RectF DisplayRect()
    {
        if (Width <= 0 || Height <= 0 || imageWidth <= 0 || imageHeight <= 0)
        {
            return new RectF(0, 0, Width, Height);
        }

        var scale = Math.Min((float)Width / imageWidth, (float)Height / imageHeight);
        var drawWidth = imageWidth * scale;
        var drawHeight = imageHeight * scale;
        var left = (Width - drawWidth) / 2f;
        var top = (Height - drawHeight) / 2f;
        return new RectF(left, top, left + drawWidth, top + drawHeight);
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var display = DisplayRect();
        var box = workingEdit.Crop;
        var crop = new RectF(
            display.Left + ((float)box.Left * display.Width()),
            display.Top + ((float)box.Top * display.Height()),
            display.Left + ((float)box.Right * display.Width()),
            display.Top + ((float)box.Bottom * display.Height()));

        // Four strips rather than one scrim rectangle with a hole cut in it — a Path with an
        // even-odd fill rule is one more thing to get backwards than four DrawRect calls.
        canvas.DrawRect(display.Left, display.Top, display.Right, crop.Top, scrim);
        canvas.DrawRect(display.Left, crop.Bottom, display.Right, display.Bottom, scrim);
        canvas.DrawRect(display.Left, crop.Top, crop.Left, crop.Bottom, scrim);
        canvas.DrawRect(crop.Right, crop.Top, display.Right, crop.Bottom, scrim);

        canvas.DrawRect(crop, frame);

        var radius = PaperTheme.Dp(Context!, HandleDrawDp) / 2f;
        canvas.DrawCircle(crop.Left, crop.Top, radius, handle);
        canvas.DrawCircle(crop.Right, crop.Top, radius, handle);
        canvas.DrawCircle(crop.Left, crop.Bottom, radius, handle);
        canvas.DrawCircle(crop.Right, crop.Bottom, radius, handle);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return false;
        }

        var display = DisplayRect();
        if (display.Width() <= 0 || display.Height() <= 0)
        {
            return false;
        }

        // Into the picture's OWN pixel space, not this view's — CropGesture knows nothing
        // about how big the view happens to be on this particular phone.
        var imageX = (e.GetX() - display.Left) / display.Width() * imageWidth;
        var imageY = (e.GetY() - display.Top) / display.Height() * imageHeight;

        switch (e.Action)
        {
            case MotionEventActions.Down:
                var touchRadius = PaperTheme.Dp(Context!, HandleTouchRadiusDp) / display.Width() * imageWidth;
                dragging = CropGesture.Nearest(
                    workingEdit.Crop, imageWidth, imageHeight, imageX, imageY, touchRadius);
                Parent?.RequestDisallowInterceptTouchEvent(dragging is not null);
                return dragging is not null;

            case MotionEventActions.Move:
                if (dragging is null)
                {
                    return false;
                }

                var candidate = CropGesture.Moved(
                    workingEdit.Crop, dragging.Value, imageWidth, imageHeight, imageX, imageY);
                workingEdit = workingEdit.CropTo(candidate);
                Invalidate();
                return true;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                Parent?.RequestDisallowInterceptTouchEvent(false);
                if (dragging is not null)
                {
                    dragging = null;
                    onReleased(workingEdit);
                }
                return true;

            default:
                return base.OnTouchEvent(e);
        }
    }
}
