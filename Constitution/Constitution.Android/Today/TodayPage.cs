using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Scraps;
using Constitution.Domain.Today;
using Constitution.Scrapbook;

namespace Constitution.Today;

/// <summary>
/// Today's page (book p.11): the scraps, top to bottom. The date is the header's subtitle (D43), not lettered here. At Release A the only scrap is
/// the torn notepad leaf with the top line on it (D43: nothing lettered goes bare on the crinkle);
/// the workout clipping and the dinner card are later releases' scraps and join the same host. The
/// leaf's line is in the calligraphy hand only when it is voiced, plain Serif otherwise (p.18).
/// Nothing here listens for a tap: the rows open nothing.
/// </summary>
public sealed class TodayPage : LinearLayout, IFullBleedPage
{
    public const string TopLineScrapId = "today-top-line";

    public const string WorkoutScrapId = "today-workout";

    public const string DinnerScrapId = "today-dinner";

    public TodayPage(Context context, TodayTopLine topLine, TodayClipping? clipping, string folio, DinnerClipping? dinner = null) : base(context)
    {
        Orientation = Orientation.Vertical;

        var host = new ScrapHost(context);
        var scraps = new List<ScrapEntry> { new(ScrapKind.NotepadLeaf, TopLineScrapId, Line(context, topLine)) };
        if (clipping is not null)
        {
            // The workout, as one magazine clipping (C-4.1.2). On a rest day there is none: its absence is the signal.
            var column = ScrapContent.Column(context, ScrapContent.Kicker(context, clipping.Kicker), ScrapContent.Head(context, clipping.Headline));
            if (clipping.ScoreLine is not null)
            {
                column.AddView(ScrapContent.Body(context, clipping.ScoreLine));
            }

            scraps.Add(new ScrapEntry(ScrapKind.MagazineClipping, WorkoutScrapId, column, folio));
        }

        if (dinner is not null)
        {
            // Tonight's dinner (C-4.1.3): an index card, or a restaurant receipt when the household is eating out (D46).
            scraps.Add(new ScrapEntry(dinner.Paper, DinnerScrapId, DinnerContent(context, dinner)));
        }

        host.SetScraps(scraps);

        var scroll = new ScrollView(context);
        scroll.AddView(host, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        AddView(scroll, new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
    }

    private static View DinnerContent(Context context, DinnerClipping dinner)
    {
        if (dinner.Paper == ScrapKind.Receipt)
        {
            var receipt = ScrapContent.Column(context, ScrapContent.ReceiptLine(context, dinner.Kicker), ScrapContent.ReceiptLine(context, dinner.Headline));
            if (dinner.Line is not null)
            {
                receipt.AddView(ScrapContent.ReceiptLine(context, dinner.Line));
            }

            return receipt;
        }

        var card = ScrapContent.Column(context, ScrapContent.IndexLabel(context, dinner.Kicker), ScrapContent.IndexLine(context, dinner.Headline, strong: true));
        if (dinner.Line is not null)
        {
            card.AddView(ScrapContent.IndexLine(context, dinner.Line));
        }

        return card;
    }

    private static TextView Line(Context context, TodayTopLine topLine)
    {
        if (topLine.IsVoiced)
        {
            return ScrapContent.Voice(context, topLine.Text);
        }

        var plain = new TextView(context) { Text = topLine.Text };
        plain.SetTextSize(Android.Util.ComplexUnitType.Sp, DesignTokens.TypeSp.Body);
        plain.SetTextColor(PaperTheme.Ink);
        plain.Typeface = PaperTheme.SerifRegular(context);
        plain.SetIncludeFontPadding(false);
        return plain;
    }
}
