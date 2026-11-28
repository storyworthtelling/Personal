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
/// One mail line (mock: Mail): the subject in the ink, centred and fitted to the mockup's width on one line, and who and
/// when under it in the grey ("Sam · 20 min"). No card, no mark, no count (WD §1, the owner's ruling): mail is never
/// marked unread on the wrist, and a line is only read, never opened, since only its subject and sender cross (A11).
/// TalkBack reads what, from whom and when (<see cref="DashWords.MailLine"/>).
/// </summary>
public sealed class MailRowView : View
{
    private MailRow? row;
    private float radius;

    public MailRowView(Context context)
        : base(context)
    {
        Setup();
    }

    public MailRowView(Context context, IAttributeSet? attrs)
        : base(context, attrs)
    {
        Setup();
    }

    private MailRowView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>The line to draw, on a screen of <paramref name="screenRadius"/> pixels.</summary>
    public void Show(MailRow shown, float screenRadius)
    {
        row = shown;
        radius = screenRadius;
        ContentDescription = DashWords.MailLine(shown);
        RequestLayout();
        Invalidate();
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        using var subject = Subject();
        using var sender = Sender();
        var words = LineOf(subject) + LineOf(sender) + Px(DashLayout.RowGap);
        SetMeasuredDimension(width, Math.Max(Px(DashLayout.MailPitch), (int)Math.Ceiling(words)));
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (row is null || Width <= 0)
        {
            return;
        }

        using var subject = Subject();
        using var sender = Sender();
        var centre = Height / 2f;
        // The mockup's places: the subject a little above the line's centre, who and when below it; never overlapping.
        var subjectCentre = centre - Px(DashLayout.MailSubjectRise);
        var senderCentre = Math.Max(centre + Px(DashLayout.MailSenderDrop), subjectCentre + (LineOf(subject) + LineOf(sender)) / 2 + Px(DashLayout.RowGap));
        var room = Math.Min(Width, Px(DashLayout.MailWidth));

        var said = TextUtils.Ellipsize(row.Subject, subject, Math.Max(0, room), TextUtils.TruncateAt.End!) ?? string.Empty;
        canvas.DrawText(said, Width / 2f, BaselineFor(subject, subjectCentre), subject);
        var caption = TextUtils.Ellipsize(DashWords.Caption(row.From, row.When), sender, Math.Max(0, room), TextUtils.TruncateAt.End!) ?? string.Empty;
        canvas.DrawText(caption, Width / 2f, BaselineFor(sender, senderCentre), sender);
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is not null)
        {
            info.ClassName = "android.widget.TextView";
        }
    }

    // Read by TalkBack as one line; nothing to tap.
    private void Setup()
    {
        Focusable = true;
        ImportantForAccessibility = ImportantForAccessibility.Yes;
    }

    private TextPaint Subject() => Centred(WatchPaints.Words(Context!, WatchType.Line, WatchWeight.Medium, WatchTokens.Neutral.Ink));

    private TextPaint Sender() => Centred(WatchPaints.Words(Context!, WatchType.Small, WatchWeight.Regular, WatchTokens.Neutral.Soft));

    private static TextPaint Centred(TextPaint paint)
    {
        paint.TextAlign = Paint.Align.Center;
        return paint;
    }

    private int Px(double fraction) => (int)Math.Round(fraction * radius);

    private static float LineOf(TextPaint paint) => paint.Descent() - paint.Ascent();

    // The baseline that centres a line of this paint on `centre`.
    private static float BaselineFor(TextPaint paint, float centre) => centre - (paint.Ascent() + paint.Descent()) / 2;
}
