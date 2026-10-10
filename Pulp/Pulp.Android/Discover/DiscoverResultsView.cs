using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Discover;

namespace Pulp.Discover;

/// <summary>
/// Search and mood results (D172; no mockup, D272): the count line ("20 of 34"), the household's own
/// titles, a hairline, the ones elsewhere, and "More" while there are more. Each row takes the stack's
/// anatomy at row size — a small poster, the title in Serif, the kind line and the home line — and a
/// tap opens the title page.
/// </summary>
internal static class DiscoverResultsView
{
    /// <summary>A result's poster: the stack's 2:3 at row height (X12 wide).</summary>
    private const int PosterWidthDp = DesignTokens.Space.X12;

    public static View Build(
        Context context,
        DiscoverResultPage page,
        Action<DiscoverRow> open,
        Action<PosterDeck, string, int> wantsPoster,
        Action more)
    {
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        column.AddView(PaperUi.Secondary(context, page.CountLine), Gap(context, DesignTokens.Space.X2));

        foreach (var row in page.Yours)
        {
            column.AddView(Row(context, row, open, wantsPoster));
        }

        if (page.Yours.Count > 0 && page.Elsewhere.Count > 0)
        {
            column.AddView(Hairline(context));
        }

        foreach (var row in page.Elsewhere)
        {
            column.AddView(Row(context, row, open, wantsPoster));
        }

        if (page.HasMore)
        {
            var button = PaperUi.SecondaryButton(context, DiscoverPageResults.MoreLabel, more);
            column.AddView(button, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = PaperTheme.Dp(context, DesignTokens.Space.X3),
                Gravity = GravityFlags.CenterHorizontal,
            });
        }

        return column;
    }

    private static View Row(Context context, DiscoverRow row, Action<DiscoverRow> open, Action<PosterDeck, string, int> wantsPoster)
    {
        var line = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        line.SetGravity(GravityFlags.CenterVertical);
        line.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.ListRowMinHeightDp));
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X2);
        line.SetPadding(0, pad, 0, pad);

        var poster = new PosterDeck(context);
        poster.Show(row.PosterUrl, behind: 0, isStruck: false);
        var width = PaperTheme.Dp(context, PosterWidthDp);
        line.AddView(poster, new LinearLayout.LayoutParams(width, width * 3 / 2)
        {
            MarginEnd = PaperTheme.Dp(context, DesignTokens.Space.X3),
        });
        if (row.PosterUrl is { } url)
        {
            wantsPoster(poster, url, width);
        }

        var text = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var name = PaperUi.Body(context, row.Name);
        name.SetTypeface(PaperTheme.SerifSemibold(context), TypefaceStyle.Normal);
        text.AddView(name);
        text.AddView(PaperUi.Secondary(context, row.KindLine));
        text.AddView(PaperUi.Secondary(context, row.HomeLine));
        line.AddView(text, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        line.Clickable = true;
        line.Click += (_, _) => open(row);
        return line;
    }

    private static View Hairline(Context context)
    {
        var rule = new View(context);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        rule.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp))
        {
            TopMargin = PaperTheme.Dp(context, DesignTokens.Space.X2),
            BottomMargin = PaperTheme.Dp(context, DesignTokens.Space.X2),
        };
        return rule;
    }

    private static LinearLayout.LayoutParams Gap(Context context, int bottomDp) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = PaperTheme.Dp(context, bottomDp) };
}
