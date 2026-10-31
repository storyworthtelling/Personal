using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Ui;
using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Scraps;

namespace Constitution.BodyTab;

/// <summary>
/// The Month / Quarter / Year switch (book page 16): the suite's §7 segmented control, in Body's own hue,
/// placed in the well's padding so it lines up with the scraps beneath. It is the same switch on the tab and on
/// a signal's page, and it only changes what the page is showing.
/// </summary>
internal static class BodyWindowSwitch
{
    public static View Build(Context context, BodyWindow window, Color hue, Action<BodyWindow> onWindow) =>
        PaperUi.Segmented(
            context,
            BodyTabModel.WindowLabels,
            index => onWindow(BodyTabModel.Windows[index]),
            BodyTabModel.Windows.ToList().IndexOf(window),
            hue);

    /// <summary>The switch sits where the scraps' padding starts, with the well's own gap above and below it.</summary>
    public static LinearLayout.LayoutParams Params(Context context)
    {
        var density = context.Resources!.DisplayMetrics!.Density;
        var parameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        parameters.SetMargins(
            (int)Math.Round(ScrapMetrics.WellPaddingStartDp * density),
            PaperTheme.Dp(context, Codex.Domain.Design.DesignTokens.Space.X2),
            (int)Math.Round(ScrapMetrics.WellPaddingEndDp * density),
            PaperTheme.Dp(context, Codex.Domain.Design.DesignTokens.Space.X1));
        return parameters;
    }
}
