using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// A chart's title on Snowball's History side and Savings' Growth side (Phase 4, "Charts"; mockup 6's and
/// mockup 9's <c>.ch-t</c>): Serif Semibold at body size, 16dp above it when something precedes it. They were
/// the small-caps <c>FieldLabel</c>, which is the Label role's face — a chart's title is a heading for the
/// thing under it, not a caption for a field.
/// </summary>
public static class PlanChartTitle
{
    /// <summary>The mockup's <c>.ch-t{margin:16px 0 0}</c>.</summary>
    public const double TopMarginDp = 16;

    public static Label Create(string text, bool first = false) => new()
    {
        Text = text,
        FontFamily = "SerifSemibold",
        FontSize = PaperResources.Token<double>("BodySize"),
        TextColor = PaperResources.Colour("Ink"),
        Margin = new Thickness(0, first ? 0 : TopMarginDp, 0, 0),
    };
}
