using Android.Content;
using Android.Text;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Scraps;
using Constitution.Scrapbook;

namespace Constitution.MealsTab;

/// <summary>
/// The This week half (book p.13): tonight's dinner and who cooks on a large taped index card, then the approved week written
/// on one card, a ruled line a day with today's in bold. Nothing here listens for a tap: no evening can be swapped from this
/// view and "I ate something else" lives in the pinned bar. Every word is <see cref="MealsWeekModel"/>'s.
/// </summary>
internal sealed class ThisWeekPage : LinearLayout
{
    public const string TonightScrapId = "meals-tonight";

    public const string WeekScrapId = "meals-week";

    private const int DayLabelWidthDp = DesignTokens.Space.X8;

    public ThisWeekPage(Context context, MealsWeekModel model) : base(context)
    {
        Orientation = Orientation.Vertical;
        var scraps = new List<ScrapEntry>();
        if (model.Tonight is { } tonight)
        {
            scraps.Add(new ScrapEntry(ScrapKind.IndexCard, TonightScrapId, TonightCard(context, tonight)));
        }
        else
        {
            scraps.Add(new ScrapEntry(ScrapKind.IndexCard, TonightScrapId, ScrapContent.Column(
                context,
                ScrapContent.IndexLabel(context, "Tonight"),
                ScrapContent.IndexLine(context, model.TonightSentence, strong: true))));
        }

        scraps.Add(new ScrapEntry(ScrapKind.IndexCard, WeekScrapId, WeekCard(context, model)));

        var host = new ScrapHost(context);
        host.SetScraps(scraps);
        var scroll = new ScrollView(context);
        scroll.AddView(host, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        AddView(scroll, new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
    }

    private static View TonightCard(Context context, TonightCard tonight)
    {
        var column = ScrapContent.Column(
            context,
            ScrapContent.IndexLabel(context, tonight.Kicker),
            ScrapContent.IndexLine(context, tonight.Dish, strong: true));
        if (!string.IsNullOrWhiteSpace(tonight.CookLine))
        {
            column.AddView(ScrapContent.IndexLine(context, tonight.CookLine));
        }

        return column;
    }

    private static View WeekCard(Context context, MealsWeekModel model)
    {
        var column = ScrapContent.Column(context, ScrapContent.IndexLabel(context, model.WeekHeading));
        foreach (var line in model.Days)
        {
            column.AddView(Row(context, line));
        }

        foreach (var note in model.Notes)
        {
            column.AddView(ScrapContent.IndexLine(context, note));
        }

        return column;
    }

    // One ruled line: the day, the dish (today's in bold, an earlier day dimmed), then the cook and the small markers. The
    // lines arrive in the planning week's order, Sunday through Saturday, and are added in that order.
    private static View Row(Context context, WeekLine line)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        var day = ScrapContent.IndexLine(context, line.DayLabel, line.IsToday);
        var dish = ScrapContent.IndexLine(context, line.Dish, line.IsToday);
        dish.Ellipsize = TextUtils.TruncateAt.End;
        var gone = line.IsPast && !line.IsToday;
        if (gone)
        {
            day.SetTextColor(PaperTheme.InkMuted);
            dish.SetTextColor(PaperTheme.InkMuted);
        }
        row.AddView(day, new LayoutParams(PaperTheme.Dp(context, DayLabelWidthDp), ViewGroup.LayoutParams.WrapContent));
        row.AddView(dish, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var trailing = string.Join(" · ", new[] { line.Cook }.Concat(line.Markers).Where(part => !string.IsNullOrWhiteSpace(part)));
        if (trailing.Length > 0)
        {
            var small = ScrapContent.IndexLine(context, trailing, line.IsToday);
            if (gone)
            {
                small.SetTextColor(PaperTheme.InkMuted);
            }

            row.AddView(small, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        }

        return row;
    }
}
