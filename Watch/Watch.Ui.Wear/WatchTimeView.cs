using Android.Content;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using Watch.Domain.Design;

namespace Watch.Ui.Wear;

/// <summary>
/// The time at the top of an app's screen, where Wear OS puts it (mock: <c>topTime</c>): the 12-hour clock (A37) in the
/// small size and the quiet grey, kept current by the platform's own clock view.
/// </summary>
public class WatchTimeView : TextClock
{
    private const string Clock = "h:mm"; // not copy: a clock pattern, 12-hour on every phone setting (A37)

    public WatchTimeView(Context context)
        : base(context)
    {
        Setup();
    }

    public WatchTimeView(Context context, IAttributeSet? attrs)
        : base(context, attrs)
    {
        Setup();
    }

    protected WatchTimeView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is not null)
        {
            info.ClassName = "android.widget.TextView";
        }
    }

    private void Setup()
    {
        Format12Hour = Clock;
        Format24Hour = Clock;
        Typeface = WatchFonts.Of(Context!, WatchWeight.Regular);
        SetTextSize(ComplexUnitType.Sp, WatchTextView.SizeOf(WatchType.Small));
        SetTextColor(WatchPaints.Of(WatchTokens.Neutral.Soft));
        Gravity = GravityFlags.Center;
    }
}
