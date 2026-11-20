using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using Watch.Domain.Design;

namespace Watch.Ui.Wear;

/// <summary>
/// Dash's glyph as a view (WD §3; mock: Tile: People waiting, Tile: nobody waiting): the dash in Dash's
/// day colour, full when someone is waiting and faint when nobody is. It is the only mark of the app.
/// </summary>
public class DashGlyphView : View
{
    private const string Name = "Dash"; // not copy: the glyph says which app it is, as an icon's label does

    private bool quiet;

    public DashGlyphView(Context context)
        : base(context)
    {
        ContentDescription = Name;
    }

    public DashGlyphView(Context context, IAttributeSet? attrs)
        : base(context, attrs)
    {
        ContentDescription = Name;
    }

    protected DashGlyphView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Nobody waiting: the dash is drawn faint and nothing else (mock).</summary>
    public bool Quiet
    {
        get => quiet;
        set
        {
            quiet = value;
            Invalidate();
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var side = Math.Min(Width, Height);
        if (side <= 0)
        {
            return;
        }
        var colour = WatchPaints.Glass(WatchApp.Dash, WatchPaints.Now());
        GlyphPainter.Dash(canvas, Width / 2f, Height / 2f, side, colour, quiet ? WatchTokens.Light.Quiet : 1);
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is not null)
        {
            info.ClassName = "android.widget.ImageView";
        }
    }
}
