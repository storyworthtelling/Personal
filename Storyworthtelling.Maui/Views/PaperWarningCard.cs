using Microsoft.Maui.Controls.Shapes;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// DESIGN.md §7's warning card: ink text on deep paper with a 3dp Action rule down its left edge —
/// the MAUI twin of <c>Codex.Ui.Android</c>'s <c>PaperUi.WarningCard</c>.
///
/// <para>It was a <c>Border</c> style with a 1dp ring all round in <c>Accent</c>, which a style
/// cannot turn into a rule on one side; so it is a view, and the content goes inside it as it went
/// inside the Border (audit W8.1, design-fidelity §1).</para>
/// </summary>
public sealed class PaperWarningCard : ContentView
{
    public PaperWarningCard()
    {
        ControlTemplate = new ControlTemplate(Build);
    }

    private static object Build()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(DesignTokens.Shape.WarningRuleDp)),
                new ColumnDefinition(GridLength.Star),
            },
        };

        grid.Add(new BoxView { Color = PaperResources.Colour("ActionMark") }, 0);
        grid.Add(new ContentPresenter { Padding = new Thickness(DesignTokens.Space.X4, DesignTokens.Space.X3) }, 1);

        return new Border
        {
            BackgroundColor = PaperResources.Colour("PaperDeep"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = DesignTokens.Shape.RadiusCard },
            Padding = 0,
            Content = grid,
        };
    }
}
