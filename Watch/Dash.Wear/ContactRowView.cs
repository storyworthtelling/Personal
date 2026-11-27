using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using Watch.Domain.Dash;
using Watch.Domain.Design;
using Watch.Ui.Wear;

namespace Dash.Wear;

/// <summary>
/// One contact of the Rolodex (P14 B6, W-5.1.2): its name on the card, in the ink, as People waiting's row draws a name
/// (mock: <c>row</c>), and nothing else, since a contact crosses as its name alone. A whole touch target; a tap starts a
/// new text to them. TalkBack reads the name, as a button.
/// </summary>
public sealed class ContactRowView : View
{
    private DashContact? contact;
    private float radius;

    public ContactRowView(Context context)
        : base(context)
    {
        Setup();
    }

    public ContactRowView(Context context, IAttributeSet? attrs)
        : base(context, attrs)
    {
        Setup();
    }

    private ContactRowView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>The contact to draw, on a screen of <paramref name="screenRadius"/> pixels.</summary>
    public void Show(DashContact shown, float screenRadius)
    {
        contact = shown;
        radius = screenRadius;
        ContentDescription = shown.Name;
        RequestLayout();
        Invalidate();
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        using var name = Name();
        var height = (int)Math.Ceiling(LineOf(name)) + Px(DashLayout.RowPadding) * 2;
        SetMeasuredDimension(width, Math.Max(height, (int)Math.Ceiling(WatchPaints.TouchTarget(Context!))));
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (contact is null || Width <= 0)
        {
            return;
        }

        var corner = Px(DashLayout.RowCorner);
        using (var card = new Paint(PaintFlags.AntiAlias) { Color = WatchPaints.Of(WatchTokens.Neutral.Card) })
        {
            canvas.DrawRoundRect(new RectF(0, 0, Width, Height), corner, corner, card);
        }

        var pad = Px(DashLayout.RowPadding);
        using var name = Name();
        var top = (Height - LineOf(name)) / 2f;
        var words = TextUtils.Ellipsize(contact.Name, name, Math.Max(0, Width - pad * 2), TextUtils.TruncateAt.End!) ?? string.Empty;
        canvas.DrawText(words, pad, top - name.Ascent(), name);
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is not null)
        {
            info.ClassName = "android.widget.Button";
        }
    }

    private void Setup()
    {
        Clickable = true;
        Focusable = true;
    }

    private TextPaint Name() => WatchPaints.Words(Context!, WatchType.Line, WatchWeight.SemiBold, WatchTokens.Neutral.Ink);

    private int Px(double fraction) => (int)Math.Round(fraction * radius);

    private static float LineOf(TextPaint paint) => paint.Descent() - paint.Ascent();
}
