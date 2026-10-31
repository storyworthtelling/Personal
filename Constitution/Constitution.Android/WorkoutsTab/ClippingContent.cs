using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Workouts.Screens;
using Constitution.Scrapbook;

namespace Constitution.WorkoutsTab;

/// <summary>
/// The words on the Workouts tab's clippings, in the typefaces and sizes the registered tokens give them. Today's is the
/// large clipping (kicker under a heavy rule, a Serif Black head, two quiet lines); each later day is a row: its day in
/// small mono, then the time, Score setting and reason. A skipped day is struck through and muted, with its note beneath.
/// </summary>
internal static class ClippingContent
{
    public static View Today(Context context, DayClipping day)
    {
        var column = ScrapContent.Column(context);
        if (day.Kicker is not null)
        {
            column.AddView(ScrapContent.Kicker(context, day.Kicker));
        }

        var head = ScrapContent.Head(context, day.Headline);
        column.AddView(head);
        var lines = new List<TextView> { head };
        foreach (var text in new[] { day.KitLine, day.ScoreLine })
        {
            if (text is not null)
            {
                var line = ScrapContent.Body(context, text);
                column.AddView(line);
                lines.Add(line);
            }
        }

        if (day.IsSkipped)
        {
            Strike(lines);
            column.AddView(Note(context, day.SkippedNote!));
        }

        return column;
    }

    public static View Row(Context context, DayClipping day)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        var label = Line(context, day.DayLabel, PaperTheme.Mono(context), DesignTokens.TypeSp.Label, PaperTheme.InkMuted);
        label.SetWidth(PaperTheme.Dp(context, DesignTokens.Space.X8));
        row.AddView(label);

        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        if (day.State != DayState.Session)
        {
            column.AddView(Line(context, day.Headline, PaperTheme.SerifRegular(context), DesignTokens.TypeSp.Body, PaperTheme.InkSoft));
        }
        else
        {
            var lines = new List<TextView> { Line(context, day.Headline, PaperTheme.SansSemibold(context), DesignTokens.TypeSp.Body, PaperTheme.Ink) };
            if (day.ScoreLine is not null)
            {
                lines.Add(Line(context, day.ScoreLine, PaperTheme.SansRegular(context), DesignTokens.TypeSp.Label, PaperTheme.InkSoft));
            }

            foreach (var line in lines)
            {
                column.AddView(line);
            }

            if (day.IsSkipped)
            {
                Strike(lines);
                column.AddView(Note(context, day.SkippedNote!));
            }
            else if (day.ReasonLine is not null)
            {
                column.AddView(Note(context, day.ReasonLine));
            }
        }

        row.AddView(column, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            LeftMargin = PaperTheme.Dp(context, DesignTokens.Space.X3),
        });
        return row;
    }

    private static TextView Note(Context context, string text) =>
        Line(context, text, PaperTheme.SansRegular(context), DesignTokens.TypeSp.Label, PaperTheme.InkMuted);

    private static void Strike(IEnumerable<TextView> lines)
    {
        foreach (var line in lines)
        {
            line.PaintFlags |= PaintFlags.StrikeThruText;
            line.SetTextColor(PaperTheme.InkMuted);
        }
    }

    private static TextView Line(Context context, string text, Typeface face, float sizeSp, Color color)
    {
        var view = new TextView(context);
        view.SetText(text, TextView.BufferType.Normal);
        view.SetTextSize(ComplexUnitType.Sp, sizeSp);
        view.SetTextColor(color);
        view.Typeface = face;
        view.SetIncludeFontPadding(false);
        return view;
    }
}
