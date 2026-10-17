using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Scraps;
using Constitution.Domain.Today;
using Constitution.Scrapbook;

namespace Constitution.Today;

/// <summary>
/// Today's page (book p.11): the date, then the scraps, top to bottom. At Release A the only scrap is
/// the torn notepad leaf with the top line on it (D43: nothing lettered goes bare on the crinkle);
/// the workout clipping and the dinner card are later releases' scraps and join the same host. The
/// leaf's line is in the calligraphy hand only when it is voiced, plain Serif otherwise (p.18).
/// Nothing here listens for a tap: the rows open nothing.
/// </summary>
public sealed class TodayPage : LinearLayout, IFullBleedPage
{
    public const string TopLineScrapId = "today-top-line";

    public TodayPage(Context context, DateTime now, TodayTopLine topLine) : base(context)
    {
        Orientation = Orientation.Vertical;

        var date = new TextView(context) { Text = TodayModel.Header(now) };
        date.SetTextSize(Android.Util.ComplexUnitType.Sp, DesignTokens.TypeSp.Heading);
        date.SetTextColor(PaperTheme.InkMuted);
        date.Typeface = PaperTheme.SerifSemibold(context);
        date.SetPadding(PaperTheme.Dp(context, ScrapMetrics.WellPaddingStartDp), PaperTheme.Dp(context, DesignTokens.Space.X3), 0, 0);
        AddView(date, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var host = new ScrapHost(context);
        host.SetScraps([new ScrapEntry(ScrapKind.NotepadLeaf, TopLineScrapId, Line(context, topLine))]);

        var scroll = new ScrollView(context);
        scroll.AddView(host, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        AddView(scroll, new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
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
