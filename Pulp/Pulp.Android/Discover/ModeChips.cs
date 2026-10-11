using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Discover;

namespace Pulp.Discover;

/// <summary>
/// The Title / Mood chips beside the search field (D171; mockup 03's <c>.chips</c>): two separate
/// rounded chips, the chosen one a highlighter wash behind full ink, the other a hairline outline in
/// muted ink — not the kit's joined <c>Segmented</c> row, which mockup 03 does not draw. The wash is
/// the Pulp identity rather than the leaf green drawn: §9 keeps a tab's role hue off its own page
/// (<c>NoAreaPageWearsItsOwnTabsRoleHue</c>).
/// </summary>
internal sealed class ModeChips : LinearLayout
{
    private readonly Dictionary<DiscoverSearchMode, TextView> chips = [];
    private readonly Color hue = Color.ParseColor(DesignTokens.Identity.Pulp.Mark);

    public ModeChips(Context context, DiscoverSearchMode initial, Action<DiscoverSearchMode> onTap) : base(context)
    {
        Orientation = Orientation.Horizontal;
        foreach (var mode in DiscoverPageInput.Modes)
        {
            var chip = PaperUi.Label(context, DiscoverPageInput.Label(mode));
            var horizontal = PaperTheme.Dp(context, DesignTokens.Space.X3);
            var vertical = PaperTheme.Dp(context, DesignTokens.Space.X1);
            chip.SetPadding(horizontal, vertical, horizontal, vertical);
            chip.Clickable = true;
            var which = mode;
            chip.Click += (_, _) =>
            {
                Select(which);
                onTap(which);
            };
            chips[mode] = chip;
            AddView(chip, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                MarginEnd = PaperTheme.Dp(context, DesignTokens.Space.X2),
            });
        }

        Select(initial);
    }

    public void Select(DiscoverSearchMode mode)
    {
        foreach (var (each, chip) in chips)
        {
            var on = each == mode;
            chip.Background = on
                ? PaperUi.Fill(Context!, PaperTheme.Wash(hue), DesignTokens.Shape.RadiusButton)
                : PaperUi.Outline(Context!, PaperTheme.Paper, PaperTheme.Hairline, DesignTokens.Shape.RadiusButton);
            chip.SetTextColor(on ? PaperTheme.Ink : PaperTheme.InkSoft);
        }
    }
}
