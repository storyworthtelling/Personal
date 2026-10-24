using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.MealsTab;

/// <summary>
/// The Meals tab's page (book p.13): the on-page switcher (D54), a section 7 segmented control in the tab's hue, with
/// whichever half it is turned to beneath it. The switcher stays put on both halves; search belongs to the box alone and
/// sits under it (D59).
/// </summary>
internal sealed class MealsPage : LinearLayout
{
    public MealsPage(Context context, Color hue, MealsView view, Action<MealsView> onSwitch, View body) : base(context)
    {
        Orientation = Orientation.Vertical;
        var switcher = PaperUi.Segmented(context, MealsViews.Labels, index => onSwitch(MealsViews.All[index]), MealsViews.All.ToList().IndexOf(view), hue);
        var gap = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        gap.SetMargins(0, PaperTheme.Dp(context, DesignTokens.Space.X2), 0, PaperTheme.Dp(context, DesignTokens.Space.X2));
        AddView(switcher, gap);
        AddView(body, new LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
    }
}
