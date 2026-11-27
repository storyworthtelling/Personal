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
/// One row of People waiting (mock: <c>row</c>): on the card, the name at the top left in the ink when unread and the
/// grey when not, when it came at the top right, and the one line under it. No mark, no count, no badge (WD §1; the
/// owner's ruling of 27 Nov 2026 took the mockup's unread rule away). A tap opens the conversation and a long press
/// offers to file or delete it (W-5.1.4); TalkBack reads who, whether it is new, when and the line
/// (<see cref="DashWords.Row"/>), and names the long press (<see cref="DashWords.FileOrDelete"/>).
/// </summary>
public sealed class PersonRowView : View
{
    private PersonRow? row;
    private float radius;

    public PersonRowView(Context context)
        : base(context)
    {
        Setup();
    }

    public PersonRowView(Context context, IAttributeSet? attrs)
        : base(context, attrs)
    {
        Setup();
    }

    private PersonRowView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>The row to draw, on a screen of <paramref name="screenRadius"/> pixels.</summary>
    public void Show(PersonRow shown, float screenRadius)
    {
        row = shown;
        radius = screenRadius;
        ContentDescription = DashWords.Row(shown);
        RequestLayout();
        Invalidate();
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        using var name = Name();
        using var line = Line();
        var words = LineOf(name) + LineOf(line) + Px(DashLayout.RowGap);
        var height = Math.Max(Px(DashLayout.RowHeight), (int)Math.Ceiling(words) + Px(DashLayout.RowPadding) * 2);
        SetMeasuredDimension(width, Math.Max(height, (int)Math.Ceiling(WatchPaints.TouchTarget(Context!))));
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (row is null || Width <= 0)
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
        using var when = When();
        using var line = Line();
        var gap = Px(DashLayout.RowGap);
        var block = LineOf(name) + gap + LineOf(line);
        var top = (Height - block) / 2f;

        var whenWidth = when.MeasureText(row.When);
        var nameRoom = Math.Max(0, Width - pad * 3 - whenWidth);
        canvas.DrawText(Fit(row.Name, name, nameRoom), pad, top - name.Ascent(), name);
        canvas.DrawText(row.When, Width - pad - whenWidth, top - name.Ascent(), when);
        canvas.DrawText(Fit(row.Line, line, Width - pad * 2), pad, top + LineOf(name) + gap - line.Ascent(), line);
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null)
        {
            return;
        }
        info.ClassName = "android.widget.Button";
        // The long press says what it does, so TalkBack's "double-tap and hold" is not a guess.
        info.AddAction(new AccessibilityNodeInfo.AccessibilityAction(AccessibilityNodeInfo.AccessibilityAction.ActionLongClick!.Id, DashWords.FileOrDelete));
    }

    private void Setup()
    {
        Clickable = true;
        LongClickable = true;
        Focusable = true;
    }

    private TextPaint Name() =>
        WatchPaints.Words(Context!, WatchType.Line, WatchWeight.SemiBold, row?.Unread == true ? WatchTokens.Neutral.Ink : WatchTokens.Neutral.Soft);

    private TextPaint When() => WatchPaints.Words(Context!, WatchType.Small, WatchWeight.Regular, WatchTokens.Neutral.Soft);

    private TextPaint Line() => WatchPaints.Words(Context!, WatchType.Small, WatchWeight.Regular, WatchTokens.Neutral.Soft);

    private int Px(double fraction) => (int)Math.Round(fraction * radius);

    private static float LineOf(TextPaint paint) => paint.Descent() - paint.Ascent();

    // A name or a line that does not fit ends in an ellipsis; the words are never shrunk (WD §5).
    private static string Fit(string words, TextPaint paint, float room) =>
        TextUtils.Ellipsize(words, paint, Math.Max(0, room), TextUtils.TruncateAt.End!) ?? string.Empty;
}
