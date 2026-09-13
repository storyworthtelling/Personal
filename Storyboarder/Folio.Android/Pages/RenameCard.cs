using Microsoft.Maui.Controls.Shapes;
using Storyworthtelling.Maui;
using Storyworthtelling.Paper;

namespace Folio.Android.Pages;

/// <summary>
/// Naming something, on the same paper the action card is made of.
///
/// <para>Deliberately <b>not</b> a system prompt. A prompt is somebody else's material dropped over
/// the page, and the suite's answer to "type something" is a field on paper — the same card the
/// actions came from, with the rows replaced by one line to write on.</para>
///
/// <para>There is no Cancel. Tapping outside dismisses it, and dismissing changes nothing, which is
/// the same bargain every card on this page offers.</para>
/// </summary>
public static class RenameCard
{
    public static View Build(string title, string prompt, string? current, Action<string?> named, Action dismissed)
    {
        var field = new Entry
        {
            Text = current ?? string.Empty,
            Placeholder = prompt,
            FontFamily = "Serif",
            FontSize = 17,
            ReturnType = ReturnType.Done,
        };

        var save = new Button { Text = "Name it" };
        save.Clicked += (_, _) =>
        {
            dismissed();
            named(field.Text);
        };
        field.Completed += (_, _) =>
        {
            dismissed();
            named(field.Text);
        };

        var haze = new BoxView { Color = PaperResources.Colour("PaperDeep").WithAlpha(0.86f) };
        var tapOut = new TapGestureRecognizer();
        tapOut.Tapped += (_, _) => dismissed();
        haze.GestureRecognizers.Add(tapOut);

        var card = new Border
        {
            Margin = new Thickness(24, 0),
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromArgb(MaterialTokens.NoteCardHex),
            Stroke = PaperResources.Colour("Hairline"),
            StrokeThickness = DesignTokens.Shape.HairlineDp,
            StrokeShape = new RoundRectangle { CornerRadius = DesignTokens.Shape.RadiusCard },
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(18, 14, 18, 16),
                Spacing = DesignTokens.Space.X3,
                Children =
                {
                    new Label { Text = title, FontFamily = "SerifSemibold", FontSize = 17, LineHeight = 1.15 },
                    field,
                    // Clearing the field is how a series goes back to being called after its first
                    // book, so the empty case is said rather than left to be discovered.
                    new Label
                    {
                        Text = "Leave it empty to go back to naming it after the first book.",
                        FontSize = 12.5,
                        TextColor = PaperResources.Colour("InkMuted"),
                    },
                    save,
                },
            },
        };

        var overlay = new Grid();
        overlay.Children.Add(haze);
        overlay.Children.Add(card);
        return overlay;
    }
}
