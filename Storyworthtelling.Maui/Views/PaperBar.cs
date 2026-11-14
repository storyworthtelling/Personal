using System.Collections.ObjectModel;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// The pinned bottom bar (DESIGN.md §7) as one component: a hairline over a row of buttons, padded by
/// the bar tokens, the <b>last</b> button the primary — on the right, taking the width that is left —
/// and the rest sized to their words.
///
/// <para>Usable straight from XAML (<c>&lt;kit:PaperBar&gt;</c> around the buttons) and what
/// <see cref="PaperBinderPage.SetBar"/> builds, so a page's bar cannot differ from another's (audit
/// W8.3a, report 14 §4–5: four paddings and two orders were drawn, one with the primary on the left).</para>
/// </summary>
[ContentProperty(nameof(Buttons))]
public sealed class PaperBar : ContentView
{
    public PaperBar()
    {
        Buttons.CollectionChanged += (_, _) => Rebuild();
    }

    /// <summary>The buttons, in order; the last is the primary.</summary>
    public ObservableCollection<View> Buttons { get; } = [];

    /// <summary>A bar of <paramref name="buttons"/>, or nothing when there are none.</summary>
    public static PaperBar? Of(IReadOnlyList<View> buttons)
    {
        if (buttons.Count == 0)
        {
            return null;
        }

        var bar = new PaperBar();
        foreach (var button in buttons)
        {
            bar.Buttons.Add(button);
        }

        return bar;
    }

    private void Rebuild()
    {
        var row = new Grid
        {
            ColumnSpacing = DesignTokens.Space.X2,
            Padding = new Thickness(DesignTokens.Shape.BarPaddingX, DesignTokens.Shape.BarPaddingY),
        };

        for (var i = 0; i < Buttons.Count; i++)
        {
            var button = Buttons[i];

            // A view has one parent: a button set into a bar again (on every appearance) is still in the
            // previous row here, so it is taken out first.
            if (button.Parent is Microsoft.Maui.Controls.Layout previous)
            {
                previous.Children.Remove(button);
            }

            row.ColumnDefinitions.Add(new ColumnDefinition(i == Buttons.Count - 1 ? GridLength.Star : GridLength.Auto));
            row.Children.Add(button);
            Grid.SetColumn(button, i);
        }

        Content = new VerticalStackLayout
        {
            Children =
            {
                new BoxView { HeightRequest = DesignTokens.Shape.HairlineDp, Color = PaperResources.Colour("Hairline") },
                row,
            },
        };
    }
}
