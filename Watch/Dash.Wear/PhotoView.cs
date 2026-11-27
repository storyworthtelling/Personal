using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using Watch.Domain.Dash;
using Watch.Domain.Design;
using Watch.Ui.Wear;

namespace Dash.Wear;

/// <summary>
/// One photo in a conversation (W-5.1.3, A10; mock: A group text with a photo): the picture filling the mockup's
/// rounded box, cropped about its centre, never stretched. Until it comes the box is the card with a status sentence
/// ("Opening the photo."), and a photo the phone could not send leaves the card saying why. TalkBack reads whose photo
/// it is and when (<see cref="DashWords.Photo"/>). The bytes are decoded off the main thread and held for the process
/// only (<see cref="PhotoBook"/>).
/// </summary>
public sealed class PhotoView : FrameLayout
{
    private readonly WatchTextView words;
    private Bitmap? picture;
    private float corner;

    public PhotoView(Context context)
        : base(context)
    {
        words = new WatchTextView(context) { Type = WatchType.Small, Tone = WatchTone.Soft };
        Setup();
    }

    public PhotoView(Context context, IAttributeSet? attrs)
        : base(context, attrs)
    {
        words = new WatchTextView(context) { Type = WatchType.Small, Tone = WatchTone.Soft };
        Setup();
    }

    private PhotoView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
        words = null!;
    }

    /// <summary>Whose photo it is (a group's sender, else none) and when, on a screen of <paramref name="screenRadius"/> pixels.</summary>
    public void Describe(string? from, string when, float screenRadius)
    {
        ContentDescription = DashWords.Photo(from, when);
        corner = (float)(DashLayout.PhotoCorner * screenRadius);
        Invalidate();
    }

    /// <summary>The photo is on its way.</summary>
    public void Waiting() => Say(DashWords.OpeningPhoto);

    /// <summary>The photo did not come: the card says why.</summary>
    public void Failed(DashTrouble trouble) => Say(DashWords.For(trouble));

    /// <summary>The photo came: decoded on the pool, then drawn if this view is still there.</summary>
    public void Show(byte[] jpeg)
    {
        var decoding = Task.Run(() => BitmapFactory.DecodeByteArray(jpeg, 0, jpeg.Length));
        decoding.ContinueWith(
            done =>
            {
                if (done.IsFaulted)
                {
                    DashLog.Quiet("DASH a photo from the phone wouldn’t decode", done.Exception!.GetBaseException());
                    Post(() => Failed(DashTrouble.Unreadable));
                    return;
                }
                Post(() =>
                {
                    if (done.Result is not { } decoded)
                    {
                        Failed(DashTrouble.Unreadable);
                        return;
                    }
                    picture = decoded;
                    words.Visibility = ViewStates.Gone;
                    Invalidate();
                });
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var box = new Android.Graphics.Path();
        box.AddRoundRect(new RectF(0, 0, Width, Height), corner, corner, Android.Graphics.Path.Direction.Cw!);
        canvas.Save();
        canvas.ClipPath(box);
        if (picture is { Width: > 0, Height: > 0 } shown)
        {
            // Filled about its centre, as the mockup's picture is: cropped, never stretched or letterboxed.
            var scale = Math.Max((float)Width / shown.Width, (float)Height / shown.Height);
            var cropWidth = Width / scale;
            var cropHeight = Height / scale;
            var left = (shown.Width - cropWidth) / 2f;
            var top = (shown.Height - cropHeight) / 2f;
            using var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
            canvas.DrawBitmap(
                shown,
                new Rect((int)left, (int)top, (int)(left + cropWidth), (int)(top + cropHeight)),
                new RectF(0, 0, Width, Height),
                paint);
        }
        else
        {
            using var card = new Paint(PaintFlags.AntiAlias) { Color = WatchPaints.Of(WatchTokens.Neutral.Card) };
            canvas.DrawRect(0, 0, Width, Height, card);
        }
        canvas.Restore();
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is not null)
        {
            info.ClassName = "android.widget.ImageView";
        }
    }

    private void Setup()
    {
        SetWillNotDraw(false);
        ImportantForAccessibility = ImportantForAccessibility.Yes;
        words.ImportantForAccessibility = ImportantForAccessibility.No;
        var inset = (int)Math.Ceiling(WatchPaints.SizeOf(Context!, WatchType.Small));
        words.SetPadding(inset, 0, inset, 0);
        AddView(words, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center));
        Waiting();
    }

    private void Say(string sentence)
    {
        if (picture is not null)
        {
            return;
        }
        words.Sentence = sentence;
        words.Visibility = ViewStates.Visible;
    }
}

/// <summary>The photos this wrist has fetched, for the process only (<see cref="HeldPhotos"/>).</summary>
internal static class PhotoBook
{
    public static HeldPhotos Held { get; } = new();
}
