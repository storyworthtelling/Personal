using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using Watch.Domain.Design;
using Watch.Domain.Kit;

namespace Watch.Ui.Wear;

/// <summary>
/// The one action on a screen (mock: <c>pill</c>; Reply, Send, Reply to all): a capsule of the app's glass with its
/// words in the near-black or the ink, whichever reads on that glass at this hour (WD §12, <see cref="WatchContrast"/>),
/// pinned near the foot of the round screen. The capsule is drawn at least as tall as the mockup draws it
/// (<see cref="DrawnHeight"/>) and as its words need; the view around it is a whole touch target (WD §12). A disabled
/// pill is drawn quiet.
/// </summary>
public class WatchPillView : TextView
{
    private WatchApp app = WatchApp.Dash;
    private float drawnHeight;

    public WatchPillView(Context context)
        : base(context)
    {
        Setup();
    }

    public WatchPillView(Context context, IAttributeSet? attrs)
        : base(context, attrs)
    {
        Setup();
    }

    protected WatchPillView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>The app whose day colour fills the capsule.</summary>
    public WatchApp App
    {
        get => app;
        set
        {
            app = value;
            Refresh();
        }
    }

    /// <summary>The capsule's least height in pixels, as the mockup draws it.</summary>
    public float DrawnHeight
    {
        get => drawnHeight;
        set
        {
            drawnHeight = value;
            Invalidate();
        }
    }

    /// <summary>The words on the pill: a label, not a sentence.</summary>
    public string Label
    {
        set => Text = value;
    }

    /// <summary>Takes the glass and its words again: the day colour moves with the hour.</summary>
    public void Refresh()
    {
        SetTextColor(WatchPaints.Of(WatchContrast.WordsOn(Glass())));
        Invalidate();
    }

    public override bool Enabled
    {
        get => base.Enabled;
        set
        {
            base.Enabled = value;
            Alpha = value ? 1f : (float)WatchTokens.Light.Quiet;
        }
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        Refresh();
    }

    protected override void OnDraw(Canvas canvas)
    {
        var words = Paint?.GetFontMetrics();
        var needed = words is null ? 0 : words.Descent - words.Ascent + PaddingTop + PaddingBottom;
        var height = Math.Min(Height, Math.Max(drawnHeight, needed));
        var top = (Height - height) / 2f;
        using var fill = new Paint(PaintFlags.AntiAlias) { Color = WatchPaints.Of(Glass()) };
        canvas.DrawRoundRect(new RectF(0, top, Width, top + height), height / 2f, height / 2f, fill);
        base.OnDraw(canvas);
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is not null)
        {
            info.ClassName = "android.widget.Button";
        }
    }

    private Oklch Glass() => WatchPaints.Glass(app, WatchPaints.Now());

    private void Setup()
    {
        Typeface = WatchFonts.Of(Context!, WatchWeight.SemiBold);
        SetTextSize(ComplexUnitType.Sp, WatchTextView.SizeOf(WatchType.Line));
        SetMaxLines(1);
        Gravity = GravityFlags.Center;
        Clickable = true;
        Focusable = true;
        var target = (int)Math.Ceiling(WatchPaints.TouchTarget(Context!));
        SetMinHeight(target);
        SetMinWidth(target);
        Refresh();
    }
}
