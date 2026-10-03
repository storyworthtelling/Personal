using Microsoft.Maui.Controls.Shapes;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// Mockup 6's legend under "Every loan, week by week" (<c>.ch-leg</c>): a swatch and a name for every
/// loan, in payoff order, wrapping onto as many lines as it needs. The swatch is the loan's own segment
/// colour (<see cref="SnowballChartPalette.RampHex"/>, the same call the bars paint with), and the name
/// is plain ink: text never wears the data colour, so identity comes from the mark beside it.
/// </summary>
public static class SnowballLegend
{
    private const double SwatchSizeDp = 9;
    private const double SwatchCornerDp = 2;
    private const double SwatchGapDp = 4;
    private const double EntryGapDp = 10;

    public static View Build(IReadOnlyList<string> loanNamesInPayoffOrder)
    {
        var legend = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
        };

        for (var i = 0; i < loanNamesInPayoffOrder.Count; i++)
        {
            legend.Children.Add(Entry(loanNamesInPayoffOrder[i], i, loanNamesInPayoffOrder.Count));
        }

        return legend;
    }

    private static View Entry(string name, int indexInPayoffOrder, int loanCount) => new HorizontalStackLayout
    {
        Spacing = SwatchGapDp,
        Margin = new Thickness(0, 2, EntryGapDp, 2),
        Children =
        {
            new Border
            {
                BackgroundColor = Color.FromArgb(SnowballChartPalette.RampHex(indexInPayoffOrder, loanCount)),
                Stroke = Colors.Transparent,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(SwatchCornerDp) },
                WidthRequest = SwatchSizeDp,
                HeightRequest = SwatchSizeDp,
                VerticalOptions = LayoutOptions.Center,
            },
            new Label
            {
                Text = name,
                Style = PaperResources.Token<Style>("SecondaryLabel"),
                VerticalOptions = LayoutOptions.Center,
            },
        },
    };
}
