using Storyworthtelling.Maui;
using Storyworthtelling.Paper;

namespace Folio.Android.Pages;

/// <summary>
/// The stationery that lies on the desk.
///
/// <para>These exist for a contrast reason, not a decorative one. Muted ink on the wood measures
/// 1.40:1; on a note card it is the same value it is on any other screen in the suite. So every
/// small label on the desk sits on one of these, and nothing is written on the plank itself.</para>
/// </summary>
public static class DeskPaper
{
    /// <summary>A section label: a cream strip, small caps, the way a shelf is labelled.</summary>
    public static View Strip(string text) => new Border
    {
        Stroke = PaperResources.Colour("Hairline"),
        StrokeThickness = DesignTokens.Shape.HairlineDp,
        BackgroundColor = Color.FromArgb(MaterialTokens.NoteCardHex),
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 3 },
        Padding = new Thickness(9, 3),
        HorizontalOptions = LayoutOptions.Start,
        Content = new Label
        {
            Text = text.ToUpperInvariant(),
            FontFamily = "SansSemibold",
            FontSize = 12,
            CharacterSpacing = 0.09,
            TextColor = PaperResources.Colour("InkSoft"),
        },
    };

    /// <summary>A note card: what a book's details are written on.</summary>
    public static Border Card(params View[] lines) => new()
    {
        Stroke = PaperResources.Colour("Hairline"),
        StrokeThickness = DesignTokens.Shape.HairlineDp,
        BackgroundColor = Color.FromArgb(MaterialTokens.NoteCardHex),
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 3 },
        Padding = new Thickness(12, 10),
        Content = new VerticalStackLayout { Children = { } }.With(lines),
    };

    /// <summary>A post-it: what a passing state is stuck on.</summary>
    public static Border PostIt(string caption, string state, Color stateInk) => new()
    {
        Stroke = Color.FromArgb(MaterialTokens.PostItEdgeHex),
        StrokeThickness = DesignTokens.Shape.HairlineDp,
        BackgroundColor = Color.FromArgb(MaterialTokens.PostItHex),
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 },
        Padding = new Thickness(8, 7, 8, 8),
        Content = new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = caption, FontSize = 12, TextColor = PaperResources.Colour("InkSoft") },
                new Label { Text = state, FontSize = 12, TextColor = stateInk, Margin = new Thickness(0, 1, 0, 0) },
            },
        },
    };

    private static VerticalStackLayout With(this VerticalStackLayout stack, IEnumerable<View> children)
    {
        foreach (var child in children)
        {
            stack.Children.Add(child);
        }

        return stack;
    }
}
