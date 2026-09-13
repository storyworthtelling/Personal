using Storyworthtelling.Maui;
using Storyworthtelling.Paper;

namespace Folio.Android.Pages;

/// <summary>
/// A book lying face-up on the desk: its painted jacket, with the title banded across the foot.
///
/// <para>This is the one composition in the app where the jackets are the main thing you see, which
/// is the whole reason the desk is drawn from above. A book with no painted cover falls back to
/// plain cloth with a keyline inside it, so a shelf of half-painted books still reads as a set.</para>
/// </summary>
public sealed class FlatBook : Border
{
    public FlatBook(string title, ImageSource? jacket, double width, double height, double tilt)
    {
        WidthRequest = width;
        HeightRequest = height;
        Rotation = tilt;
        StrokeThickness = DesignTokens.Shape.HairlineDp;
        Stroke = PaperResources.Colour("Ink").WithAlpha(0.22f);
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(2, 4, 2, 4) };
        Padding = 0;

        var painted = jacket is not null;
        var titleSize = width >= 100 ? 13 : 11;

        var layers = new Grid();

        if (painted)
        {
            layers.Children.Add(new Image { Source = jacket, Aspect = Aspect.AspectFill });
        }
        else
        {
            var cloth = BookCloth.For(title);
            layers.Children.Add(new Border
            {
                StrokeThickness = 0,
                Background = new LinearGradientBrush(
                    [new GradientStop(cloth.Top, 0), new GradientStop(cloth.Bottom, 1)],
                    new Point(0, 0), new Point(0.6, 1)),
            });

            // The keyline a plain binding carries instead of art.
            layers.Children.Add(new Border
            {
                Margin = 5,
                StrokeThickness = DesignTokens.Shape.HairlineDp,
                Stroke = Color.FromArgb(PaperColorTokens.Paper).WithAlpha(0.4f),
                BackgroundColor = Colors.Transparent,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 },
            });
        }

        // The title band. Over art it fades up out of the paper so the jacket is not cut in half;
        // on plain cloth it is simply lettered in cream.
        var band = new Label
        {
            Text = title,
            FontFamily = "SerifSemibold",
            FontSize = titleSize,
            LineHeight = 1.12,
            Padding = new Thickness(7, 5, 7, 6),
            VerticalOptions = LayoutOptions.End,
            TextColor = painted ? PaperResources.Colour("Ink") : Color.FromArgb(PaperColorTokens.Paper),
        };

        if (painted)
        {
            band.Background = new LinearGradientBrush(
                [
                    new GradientStop(Color.FromArgb(PaperColorTokens.Paper).WithAlpha(0f), 0),
                    new GradientStop(Color.FromArgb(PaperColorTokens.Paper).WithAlpha(0.94f), 0.4f),
                ],
                new Point(0, 0), new Point(0, 1));
        }

        layers.Children.Add(band);
        Content = layers;
    }
}
