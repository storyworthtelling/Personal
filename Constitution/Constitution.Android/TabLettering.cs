using Android.Graphics;
using Android.Widget;
using Codex.Ui;

namespace Constitution;

/// <summary>
/// Letters the inactive tabs in their areas' own lettering token (D45: deep cloths with paper
/// lettering). The kit's <see cref="BinderTabs"/> tints every inactive tab's glyph and label in
/// the page ink, which is right for Post and Score's light cloths and unreadable on Constitution's
/// deep ones; the strip's tabs are real views, so the correction is applied over them after each
/// <see cref="BinderTabs.SetAreas"/> / <see cref="BinderTabs.SetActive"/>. The active tab keeps
/// the kit's own treatment (page-coloured, set in the area's ink).
/// </summary>
internal static class TabLettering
{
    public static void Apply(BinderTabs binder, IReadOnlyList<IConstitutionArea> areas, int activeIndex)
    {
        for (var i = 0; i < binder.ChildCount && i < areas.Count; i++)
        {
            if (i == activeIndex || binder.GetChildAt(i) is not LinearLayout column || column.ChildCount != 2)
            {
                continue;
            }

            var lettering = Color.ParseColor(areas[i].Definition.InactiveLettering);
            ((ImageView)column.GetChildAt(0)!).SetColorFilter(lettering);
            ((TextView)column.GetChildAt(1)!).SetTextColor(lettering);
        }
    }
}
