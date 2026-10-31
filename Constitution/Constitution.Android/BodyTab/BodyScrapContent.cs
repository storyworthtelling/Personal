using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Scraps;
using Constitution.Domain.Voice;
using Constitution.Scrapbook;

namespace Constitution.BodyTab;

/// <summary>
/// What goes on Body's scraps (book page 16): a doctor's chart (form head, line, latest value in mono with the
/// change in words beside it), a chart that is only asking permission, and the notepad leaf the owner's voice
/// writes the month on. Each is built from a model's words; none decides anything. Sizes and inks come from the
/// design tokens, and the line's ink is the page's own.
/// </summary>
internal static class BodyScrapContent
{
    /// <summary>Height of the line on the tab, from the mockup's 70 px chart; a signal's own page draws it larger.</summary>
    public const int SmallChartDp = 70;

    /// <summary>Height of the larger chart on a signal's page, from the mockup's 200 px.</summary>
    public const int LargeChartDp = 200;

    public const string MonthlyScrapId = "body-monthly";

    public static string ChartScrapId(BodySignal signal) => "body-chart-" + BodySignals.Id(signal);

    /// <summary>
    /// A chart scrap: the form head across the top, the line, and the value row. A chart that is not drawing
    /// (not allowed, taken away, nothing read) shows its status line in the row instead and no line.
    /// </summary>
    public static ScrapEntry Chart(Context context, SignalCard card, BodyWindow window, DateOnly today, Action? onOpen, bool large = false)
    {
        var column = ScrapContent.Column(context, ScrapContent.FormHead(context, [.. card.Head.Fields]));
        if (card.DrawsALine || card.State == SignalCardState.NoReadings)
        {
            column.AddView(new BodyChartView(
                context, card.Readings, window, today, large ? LargeChartDp : SmallChartDp,
                continuesDotted: card.State == SignalCardState.WaitingForBed, lineScale: large ? 1.25f : 1));
        }

        column.AddView(ValueRow(context, card), ValueRowParams(context));
        MakeTappable(column, onOpen, BodySignals.Name(card.Signal));
        return new ScrapEntry(ScrapKind.ChartScrap, ChartScrapId(card.Signal), column);
    }

    /// <summary>A chart scrap that says what Health Connect must be allowed to read, and that it only reads.</summary>
    public static ScrapEntry Ask(Context context, BodySignal signal, PermissionAsk ask)
    {
        var column = ScrapContent.Column(context, ScrapContent.FormHead(context, [.. FormHead.For(signal, window: null).Fields]));
        var need = ScrapContent.Body(context, ask.Need);
        need.SetPadding(0, PaperTheme.Dp(context, DesignTokens.Space.X2), 0, PaperTheme.Dp(context, DesignTokens.Space.X2));
        column.AddView(need);
        return new ScrapEntry(ScrapKind.ChartScrap, ChartScrapId(signal), column);
    }

    /// <summary>
    /// The notepad leaf the month's sentence is written on. The leaf starts with the line it was handed (the plain
    /// sentence, or a voiced one already composed) and <see cref="BodySeatNote.Show(SeatLine)"/> swaps it in place.
    /// </summary>
    public static (ScrapEntry Entry, BodySeatNote Note) Leaf(Context context, string id, SeatLine first)
    {
        var note = new BodySeatNote(context, first);
        return (new ScrapEntry(ScrapKind.NotepadLeaf, id, note), note);
    }

    /// <summary>The leaf for a page with no seat to speak: plain Serif, never the calligraphy hand.</summary>
    public static ScrapEntry PlainLeaf(Context context, string id, string sentence) =>
        new(ScrapKind.NotepadLeaf, id, new BodySeatNote(context, sentence, voiced: false));

    // ---- the value row ----

    private static View ValueRow(Context context, SignalCard card)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetBackgroundColor(ScrapInk.ChartPaper);
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X1);
        row.SetPadding(pad, PaperTheme.Dp(context, DesignTokens.Space.X1) / 2, pad, PaperTheme.Dp(context, DesignTokens.Space.X1) / 2);

        var left = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        left.SetGravity(GravityFlags.CenterVertical);
        if (card.StatusLine.Length > 0)
        {
            left.AddView(Text(context, card.StatusLine, PaperTheme.SansSemibold(context), DesignTokens.TypeSp.Secondary, PaperTheme.Ink));
        }
        else
        {
            left.AddView(Text(context, card.ValueText, PaperTheme.Mono(context), DesignTokens.TypeSp.Body, PaperTheme.Ink));
            var when = Text(context, card.WhenText, PaperTheme.SansRegular(context), DesignTokens.TypeSp.Label, PaperTheme.InkMuted);
            when.SetPadding(PaperTheme.Dp(context, DesignTokens.Space.X1), 0, 0, 0);
            left.AddView(when);
        }

        var right = Text(context, card.ChangeText, PaperTheme.SansRegular(context), DesignTokens.TypeSp.Secondary, PaperTheme.InkSoft);
        right.Gravity = GravityFlags.End | GravityFlags.CenterVertical;

        row.AddView(left, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        row.AddView(right, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            MarginStart = PaperTheme.Dp(context, DesignTokens.Space.X2),
        });
        return row;
    }

    // The row's paper reaches 4 dp past the scrap's padding on each side, as the mockup's -4px margin does.
    private static LinearLayout.LayoutParams ValueRowParams(Context context)
    {
        var parameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        var edge = PaperTheme.Dp(context, DesignTokens.Space.X1);
        parameters.SetMargins(-edge, edge, -edge, 0);
        return parameters;
    }

    private static void MakeTappable(View view, Action? onOpen, string name)
    {
        if (onOpen is null)
        {
            return;
        }

        view.Clickable = true;
        view.Click += (_, _) => onOpen();
        view.ContentDescription = $"{name}, open the chart";
    }

    private static TextView Text(Context context, string text, Typeface face, float sizeSp, Color ink)
    {
        var view = new TextView(context) { Text = text };
        view.SetTextSize(ComplexUnitType.Sp, sizeSp);
        view.SetTextColor(ink);
        view.Typeface = face;
        view.SetIncludeFontPadding(false);
        return view;
    }
}

/// <summary>
/// The line written on a notepad leaf, swapped in place when the voiced sentence arrives. Starts as whatever it
/// was handed, so the page never waits on the voice and never shows a spinner; the calligraphy hand is for a
/// voiced line only, plain Serif otherwise.
/// </summary>
internal sealed class BodySeatNote : FrameLayout
{
    public BodySeatNote(Context context, string text, bool voiced) : base(context)
    {
        Show(text, voiced);
    }

    public BodySeatNote(Context context, SeatLine first) : this(context, first.Text, first.IsVoiced)
    {
    }

    /// <summary>Replaces the line on the leaf. Must be called on the UI thread.</summary>
    public void Show(SeatLine line) => Show(line.Text, line.IsVoiced);

    private void Show(string text, bool voiced)
    {
        RemoveAllViews();
        AddView(
            voiced ? ScrapContent.Voice(Context!, text) : PlainText(Context!, text),
            new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        RequestLayout();
    }

    private static TextView PlainText(Context context, string text)
    {
        var view = new TextView(context) { Text = text };
        view.SetTextSize(ComplexUnitType.Sp, DesignTokens.TypeSp.Body);
        view.SetTextColor(PaperTheme.Ink);
        view.Typeface = PaperTheme.SerifRegular(context);
        view.SetIncludeFontPadding(false);
        return view;
    }
}
