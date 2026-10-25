using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.MealsTab;

/// <summary>
/// The picker that grows up out of the pinned bar (book p.14, C-6.5.4): the bar's own view with one panel stacked over it, a row for
/// each way into the box, in place and with no dialog. A row that cannot be used stays and says why in its subtitle. Decides
/// nothing: which rows there are, their words and whether each is enabled are <see cref="RecipePickerModel"/>'s.
/// </summary>
internal sealed class PickerSheet : LinearLayout
{
    public PickerSheet(Context context, IReadOnlyList<PickerRow> rows, Action<PickerSource> onPick, View bar) : base(context)
    {
        Orientation = Orientation.Vertical;
        var panel = new LinearLayout(context) { Orientation = Orientation.Vertical };
        panel.Background = PaperUi.Outline(context, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X4);
        panel.SetPadding(pad, pad, pad, pad);
        foreach (var row in rows)
        {
            panel.AddView(Row(context, row, onPick));
        }

        var around = PaperTheme.Dp(context, DesignTokens.Space.X6);
        AddView(panel, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            LeftMargin = around,
            RightMargin = around,
            TopMargin = PaperTheme.Dp(context, DesignTokens.Space.X2),
        });
        AddView(bar);
    }

    private static LinearLayout Row(Context context, PickerRow row, Action<PickerSource> onPick)
    {
        var box = new LinearLayout(context) { Orientation = Orientation.Vertical };
        box.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));
        box.SetGravity(GravityFlags.CenterVertical);
        var title = row.Enabled ? PaperUi.Body(context, row.Title) : PaperUi.BodyMuted(context, row.Title);
        box.AddView(title);
        box.AddView(PaperUi.Secondary(context, row.Subtitle));
        box.Click += (_, _) => onPick(row.Source);
        return box;
    }
}
