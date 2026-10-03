using Microsoft.Maui.Controls.Shapes;
using Index.Android.Pages;
using Index.Domain.Design;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// A text <see cref="Entry"/> in a box, with the two states Accounts' forms give a field beyond plain: the
/// faint identity tint of a required field (DESIGN.md §7, mockups 4/15 <c>.req</c>) and the rose of a bad
/// figure, with a line under the box saying how to fix it. Both come from the kit's tokens — the Problem
/// role for the rose, the Accounts identity cloth at <see cref="MaterialTokens.RequiredTintAlpha"/> for the
/// tint — so the post-it and the Add form draw them identically.
///
/// <para>The box is the minimum a rose border needs (an <see cref="Entry"/> has no border of its own); its
/// finished shape — mono variant, <c>$</c> prefix and <c>%</c> suffix, radius, 44dp height — is the
/// cosmetic pass (Phase 4), not this.</para>
/// </summary>
public sealed class FieldBox : VerticalStackLayout
{
    private readonly Border box;
    private readonly Label errorLine;

    public FieldBox(Entry entry)
    {
        Entry = entry;
        Spacing = 2;

        entry.BackgroundColor = Colors.Transparent;

        box = new Border
        {
            Stroke = PaperResources.Colour("Hairline"),
            StrokeThickness = Storyworthtelling.Maui.DesignTokens.Shape.HairlineDp,
            StrokeShape = new RoundRectangle { CornerRadius = Storyworthtelling.Maui.DesignTokens.Shape.RadiusInput },
            Padding = new Thickness(Storyworthtelling.Maui.DesignTokens.Space.X2, 0),
            Content = entry,
        };

        errorLine = new Label
        {
            Style = PaperResources.Token<Style>("SecondaryLabel"),
            TextColor = PaperResources.Colour("ProblemMark"),
            IsVisible = false,
        };

        Children.Add(box);
        Children.Add(errorLine);
    }

    public Entry Entry { get; }

    /// <summary>True while the box shows its rose state.</summary>
    public bool IsInvalid => errorLine.IsVisible;

    /// <summary>The line under the box ("" when there is none) — read by nothing but the page's own checks.</summary>
    public string ErrorText => errorLine.IsVisible ? errorLine.Text : string.Empty;

    /// <summary>Turns the box rose with <paramref name="message"/> under it; null puts it back.</summary>
    public void ShowError(string? message)
    {
        var bad = !string.IsNullOrEmpty(message);
        errorLine.Text = bad ? message : string.Empty;
        errorLine.IsVisible = bad;
        box.Stroke = PaperResources.Colour(bad ? "ProblemMark" : "Hairline");
    }

    /// <summary>The required tint: the Accounts identity cloth, faint. Off is no fill at all.</summary>
    public void SetRequiredTint(bool tinted)
    {
        box.BackgroundColor = tinted
            ? LedgerAreas.All[(int)LedgerArea.Accounts].Cloth.WithAlpha((float)MaterialTokens.RequiredTintAlpha)
            : Colors.Transparent;
    }
}
