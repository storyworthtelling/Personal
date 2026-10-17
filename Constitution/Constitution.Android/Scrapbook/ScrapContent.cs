using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Scrapbook;
using Storyworthtelling.Paper;

namespace Constitution.Scrapbook;

/// <summary>
/// The text a scrap carries, in the typefaces and sizes its paper calls for. Small on purpose: a
/// scrap holds one thing (D47), so its content is a handful of lines, and each helper only sets
/// the face, the size and the ink from the suite's tokens. Rows on an index card sit on the card's
/// 22 dp rules, so <see cref="IndexLine"/> is exactly one rule tall.
/// </summary>
public static class ScrapContent
{
    private const int IndexRowDp = 22;
    private const int KickerRuleDp = 3;
    private const int KickerGapDp = 4;

    /// <summary>A vertical stack that lets children reach into the scrap's padding (the chart's form head does).</summary>
    public static LinearLayout Column(Context context, params View[] rows)
    {
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        column.SetClipChildren(false);
        foreach (var row in rows)
        {
            column.AddView(row);
        }

        return column;
    }

    /// <summary>A small caps label in the muted ink.</summary>
    public static TextView Label(Context context, string text)
    {
        var view = Text(context, text, PaperTheme.SansSemibold(context), DesignTokens.TypeSp.Label, PaperTheme.InkMuted);
        view.SetAllCaps(true);
        return view;
    }

    /// <summary>The index card's label row: exactly the card's 23 dp, closed by the red head rule.</summary>
    public static TextView IndexLabel(Context context, string text)
    {
        var view = Label(context, text);
        view.SetHeight(PaperTheme.Dp(context, (int)ScrapRuling.IndexLabelRowDp));
        view.Gravity = GravityFlags.CenterVertical;
        return view;
    }

    /// <summary>One line written on one of the card's rules: a single 22 dp row, so the next line lands on the next rule.</summary>
    public static TextView IndexLine(Context context, string text, bool strong = false)
    {
        var view = Text(
            context,
            text,
            strong ? PaperTheme.SansSemibold(context) : PaperTheme.SansRegular(context),
            strong ? DesignTokens.TypeSp.Heading : DesignTokens.TypeSp.Secondary,
            strong ? PaperTheme.Ink : PaperTheme.InkSoft);
        view.SetHeight(PaperTheme.Dp(context, IndexRowDp));
        view.SetSingleLine(true);
        view.Gravity = GravityFlags.CenterVertical;
        return view;
    }

    /// <summary>Plain running text in the soft ink.</summary>
    public static TextView Body(Context context, string text) =>
        Text(context, text, PaperTheme.SansRegular(context), DesignTokens.TypeSp.Secondary, PaperTheme.InkSoft);

    /// <summary>The owner's voice on the notepad leaf: Pinyon Script in ink-blue, always.</summary>
    public static TextView Voice(Context context, string text) =>
        Text(context, text, PaperTheme.Script(context), DesignTokens.TypeSp.Voice, PaperTheme.InkBlue);

    /// <summary>A clipping's kicker: small caps under a heavy rule drawn as wide as the words.</summary>
    public static View Kicker(Context context, string text)
    {
        var view = new KickerView(context, PaperTheme.Dp(context, KickerRuleDp));
        view.SetText(text, TextView.BufferType.Normal);
        view.SetTextSize(ComplexUnitType.Sp, DesignTokens.TypeSp.Label);
        view.SetTextColor(PaperTheme.Ink);
        view.Typeface = PaperTheme.SansSemibold(context);
        view.SetAllCaps(true);
        view.SetPadding(0, PaperTheme.Dp(context, KickerRuleDp + KickerGapDp), 0, 0);
        return view;
    }

    /// <summary>A clipping's headline: Serif Black, tight.</summary>
    public static TextView Head(Context context, string text)
    {
        var view = Text(context, text, PaperTheme.SerifBlack(context), DesignTokens.TypeSp.Title, PaperTheme.Ink);
        view.SetPadding(0, PaperTheme.Dp(context, DesignTokens.Space.X1), 0, 0);
        return view;
    }

    /// <summary>A receipt's line: mono, as a till prints it.</summary>
    public static TextView ReceiptLine(Context context, string text) =>
        Text(context, text, PaperTheme.Mono(context), DesignTokens.TypeSp.Mono, PaperTheme.Ink);

    /// <summary>
    /// The chart's form head: field names and values across the top in small mono on the chart paper,
    /// closed by a 1.5 dp ink rule. It reaches out over the scrap's padding to the paper's edges, so it
    /// must sit first in a <see cref="Column"/> (`margin: -10px -12px 6px`).
    /// </summary>
    public static View FormHead(Context context, params (string Field, string Value)[] pairs)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetBackgroundColor(ScrapInk.ChartPaper);
        row.SetPadding(PaperTheme.Dp(context, DesignTokens.Space.X3), PaperTheme.Dp(context, DesignTokens.Space.X2), PaperTheme.Dp(context, DesignTokens.Space.X3), PaperTheme.Dp(context, DesignTokens.Space.X1));
        foreach (var (field, value) in pairs)
        {
            var name = Text(context, field, PaperTheme.Mono(context), DesignTokens.TypeSp.Mono * PaperTypeScale.Ratio, PaperTheme.InkMuted);
            var content = Text(context, value, PaperTheme.Mono(context), DesignTokens.TypeSp.Mono * PaperTypeScale.Ratio, PaperTheme.Ink);
            name.SetPadding(0, 0, PaperTheme.Dp(context, DesignTokens.Space.X1), 0);
            content.SetPadding(0, 0, PaperTheme.Dp(context, DesignTokens.Space.X3), 0);
            row.AddView(name);
            row.AddView(content);
        }

        var rule = new View(context);
        rule.SetBackgroundColor(PaperTheme.Ink);

        var head = new LinearLayout(context) { Orientation = Orientation.Vertical };
        head.SetClipChildren(false);
        head.AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        head.AddView(rule, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, (int)Math.Round(1.5f * context.Resources!.DisplayMetrics!.Density)));

        // The negative margins carry the head out over the padding to the paper's edges, and up to its top.
        var margins = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        margins.SetMargins(
            -PaperTheme.Dp(context, DesignTokens.Space.X3),
            -PaperTheme.Dp(context, DesignTokens.Space.X3),
            -PaperTheme.Dp(context, DesignTokens.Space.X3),
            PaperTheme.Dp(context, DesignTokens.Space.X1));
        head.LayoutParameters = margins;
        return head;
    }

    private static TextView Text(Context context, string text, Typeface face, float sizeSp, Color color)
    {
        var view = new TextView(context);
        view.SetText(text, TextView.BufferType.Normal);
        view.SetTextSize(ComplexUnitType.Sp, sizeSp);
        view.SetTextColor(color);
        view.Typeface = face;
        view.SetIncludeFontPadding(false);
        return view;
    }

    /// <summary>A text view with a heavy rule across its top, as wide as its words.</summary>
    private sealed class KickerView : TextView
    {
        private readonly Paint rule = ScrapInk.Fill(PaperTheme.Ink);
        private readonly int ruleHeight;

        public KickerView(Context context, int ruleHeightPx) : base(context)
        {
            ruleHeight = ruleHeightPx;
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            canvas.DrawRect(0, 0, Width, ruleHeight, rule);
        }
    }
}
