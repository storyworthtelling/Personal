using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using Watch.Domain.Design;
using Watch.Domain.Kit;

namespace Watch.Ui.Wear;

/// <summary>Which of the three sizes a line is set at (WD §5).</summary>
public enum WatchType
{
    Glance,
    Line,
    Small,
}

/// <summary>Primary words or the quiet secondary grey (WD §4.3).</summary>
public enum WatchTone
{
    Ink,
    Soft,
}

/// <summary>
/// The watch's words (WD §5, §6; A37): IBM Plex Mono at one of the three token sizes, wrapped once at a
/// word and never shrunk. A <see cref="Sentence"/> must be one sentence with no exclamation, checked
/// by <see cref="WatchText"/>; a <see cref="Label"/> (a name, a song) is not a sentence and skips that
/// check. A line that does not fit two lines is logged by its length only and ends in an ellipsis: the
/// fix is shorter words, never smaller type.
/// </summary>
public class WatchTextView : TextView
{
    private const string LogTag = "WatchText"; // not copy: a logcat tag

    private WatchType type = WatchType.Line;
    private bool body;

    public WatchTextView(Context context)
        : base(context)
    {
        Setup();
    }

    public WatchTextView(Context context, IAttributeSet? attrs)
        : base(context, attrs)
    {
        Setup();
    }

    protected WatchTextView(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>The size the words are set at; never changed to make them fit.</summary>
    public WatchType Type
    {
        get => type;
        set
        {
            type = value;
            SetTextSize(ComplexUnitType.Sp, SizeOf(value));
        }
    }

    /// <summary>Which of Plex Mono's weights the words are set in (mock: 400 words, 500 labels, 600 names).</summary>
    public WatchWeight Weight
    {
        set => Typeface = WatchFonts.Of(Context!, value);
    }

    /// <summary>Ink for primary words, Soft for the quiet line.</summary>
    public WatchTone Tone
    {
        set => SetTextColor(WatchPaints.Of(value == WatchTone.Ink ? WatchTokens.Neutral.Ink : WatchTokens.Neutral.Soft));
    }

    /// <summary>The one short sentence; refuses two sentences, an exclamation or nothing at all.</summary>
    public string Sentence
    {
        set
        {
            var verdict = WatchText.Check(value);
            if (verdict != WatchTextVerdict.Fits)
            {
                throw new ArgumentException($"A watch line must be one sentence with no exclamation; this one is {verdict}.", nameof(value));
            }
            Text = value;
        }
    }

    /// <summary>A name or a title: wrapped and never shrunk like a sentence, without the sentence rules.</summary>
    public string Label
    {
        set => Text = value;
    }

    /// <summary>
    /// A person's own words (a message, a reply read back): not the watch's copy, so not held to one sentence or two
    /// lines. They wrap to as many lines as they take and are never shrunk or cut.
    /// </summary>
    public string Body
    {
        set
        {
            body = true;
            SetMaxLines(int.MaxValue);
            Ellipsize = null;
            Text = value;
        }
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
        var width = MeasuredWidth - PaddingLeft - PaddingRight;
        var text = Text ?? "";
        if (body || width <= 0 || text.Length == 0 || Paint is not { } paint)
        {
            return;
        }
        var layout = WatchText.Lay(text, width, line => paint.MeasureText(line));
        if (layout.Verdict == WatchTextVerdict.TooLong)
        {
            Log.Warn(LogTag, $"A line of {text.Length} characters does not fit two lines; it ends in an ellipsis.");
        }
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null)
        {
            return;
        }
        info.ClassName = "android.widget.TextView";
        info.Heading = type == WatchType.Glance;
    }

    private void Setup()
    {
        Typeface = WatchFonts.Of(Context!, WatchWeight.Regular);
        Type = WatchType.Line;
        Tone = WatchTone.Ink;
        SetMaxLines(WatchText.MaxLines);
        Ellipsize = TextUtils.TruncateAt.End;
        BreakStrategy = BreakStrategy.Simple;
        HyphenationFrequency = HyphenationFrequency.None;
        SetAutoSizeTextTypeWithDefaults(AutoSizeTextType.None);
        Gravity = GravityFlags.Center;
    }

    internal static float SizeOf(WatchType type) => type switch
    {
        WatchType.Glance => WatchTokens.Type.Glance,
        WatchType.Small => WatchTokens.Type.Small,
        _ => WatchTokens.Type.Line,
    };
}
