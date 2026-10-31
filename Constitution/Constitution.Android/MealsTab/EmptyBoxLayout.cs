using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.MealsTab;

/// <summary>
/// The recipe box before it is ten cards deep (book p.14, C-6.2.5): a static Serif-italic invitation (not a voice seat, so
/// nothing is composed), a mono counter, ten card places, the scanned ones filled and the rest dashed, and the footnote. The
/// primary action is in the pinned bar, not here.
/// </summary>
internal static class EmptyBoxLayout
{
    public static View Build(Context context, Constitution.Domain.Recipes.Screens.EmptyBoxView view)
    {
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var invitation = PaperUi.EmptyState(context, view.State.Invitation);
        invitation.Gravity = GravityFlags.Start;
        column.AddView(invitation, Spaced(context, DesignTokens.Space.X4));
        column.AddView(PaperUi.Mono(context, view.State.Counter), Spaced(context, DesignTokens.Space.X3));

        var places = new LinearLayout(context) { Orientation = Orientation.Vertical };
        for (var row = 0; row < view.PlaceTitles.Count; row += 2)
        {
            var pair = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            for (var column2 = row; column2 < Math.Min(row + 2, view.PlaceTitles.Count); column2++)
            {
                var cell = Place(context, view.PlaceTitles[column2], view.State.Places[column2]);
                var parameters = new LinearLayout.LayoutParams(0, PaperTheme.Dp(context, DesignTokens.Space.X12 + DesignTokens.Space.X4), 1f);
                parameters.SetMargins(0, PaperTheme.Dp(context, DesignTokens.Space.X1), PaperTheme.Dp(context, DesignTokens.Space.X2), PaperTheme.Dp(context, DesignTokens.Space.X1));
                pair.AddView(cell, parameters);
            }

            places.AddView(pair);
        }

        column.AddView(places, Spaced(context, DesignTokens.Space.X3));
        column.AddView(PaperUi.Secondary(context, view.State.Footnote), Spaced(context, DesignTokens.Space.X4));

        var scroll = new ScrollView(context);
        scroll.AddView(column, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return scroll;
    }

    private static View Place(Context context, string? title, PlaceState state)
    {
        var cell = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var outline = PaperUi.Outline(context, state == PlaceState.Filled ? PaperTheme.PaperBright : PaperTheme.Paper, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        if (state == PlaceState.Dashed)
        {
            outline.SetStroke(PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp), PaperTheme.Hairline, PaperTheme.Dp(context, DesignTokens.Space.X1), PaperTheme.Dp(context, DesignTokens.Space.X1));
        }

        cell.Background = outline;
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X2);
        cell.SetPadding(pad, pad, pad, pad);
        if (title is not null)
        {
            var text = PaperUi.Body(context, title);
            text.SetMaxLines(2);
            text.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            cell.AddView(text);
        }

        return cell;
    }

    private static LinearLayout.LayoutParams Spaced(Context context, int topDp)
    {
        var parameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        parameters.TopMargin = PaperTheme.Dp(context, topDp);
        return parameters;
    }
}
