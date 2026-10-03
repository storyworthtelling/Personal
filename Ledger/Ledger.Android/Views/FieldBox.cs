using Microsoft.Maui.Controls.Shapes;
using Index.Android.Pages;
using Index.Domain.Design;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// A text <see cref="Entry"/> in the design system's box (DESIGN.md §7; mockups 4 and 15): 44dp tall, radius
/// 12, a hairline edge, 12dp inside. The mono variant (<c>mono</c>) sits on <c>PaperDeep</c> and
/// is for figures; the ordinary one sits on <c>PaperBright</c>. A figure's own furniture is drawn by the box,
/// not typed into the text: a <c>prefix</c> ("$") before it, or a <c>suffix</c> ("%")
/// after it behind a faint divider, so the entry holds the bare number and reads back as the same figure.
///
/// <para>It carries the two states Accounts' forms give a field beyond plain: the faint identity tint of a
/// required field (<see cref="MaterialTokens.RequiredTintAlpha"/> of the Accounts cloth, laid OVER the box's
/// fill in its own layer, as the mockup's two-gradient <c>.req</c> is) and the rose of a bad figure, with a
/// line under the box saying how to fix it. Both come from the kit's tokens, so the post-it and the Add form
/// draw them identically.</para>
/// </summary>
public sealed class FieldBox : VerticalStackLayout
{
    private readonly Border box;
    private readonly BoxView tintLayer;
    private readonly Label errorLine;
    private bool invalid;

    public FieldBox(Entry entry, bool mono = false, string? prefix = null, string? suffix = null)
    {
        Entry = entry;
        Spacing = 2;

        entry.BackgroundColor = Colors.Transparent;
        entry.VerticalOptions = LayoutOptions.Center;
        if (mono)
        {
            entry.FontFamily = "Mono";
        }

        tintLayer = new BoxView { Color = Colors.Transparent, InputTransparent = true };

        var columns = FieldBoxLayout.For(prefix is not null, suffix is not null);
        var inside = new Grid { ColumnSpacing = 0 };
        foreach (var column in columns.Columns)
        {
            inside.ColumnDefinitions.Add(new ColumnDefinition(column == FieldBoxColumn.Star ? GridLength.Star : GridLength.Auto));
        }

        inside.Children.Add(tintLayer);
        Grid.SetColumnSpan(tintLayer, columns.Columns.Count);

        if (prefix is not null && columns.PrefixColumn is { } prefixColumn)
        {
            var prefixLabel = Affix(prefix);
            prefixLabel.Margin = new Thickness(0, 0, FieldBoxLayout.AffixGapDp, 0);
            inside.Children.Add(prefixLabel);
            Grid.SetColumn(prefixLabel, prefixColumn);
        }

        inside.Children.Add(entry);
        Grid.SetColumn(entry, columns.EntryColumn);
        Grid.SetColumnSpan(entry, columns.EntryColumnSpan);

        if (suffix is not null && columns.SuffixColumn is { } suffixColumn)
        {
            var suffixBlock = new HorizontalStackLayout
            {
                Spacing = 6,
                Margin = new Thickness(FieldBoxLayout.AffixGapDp, 0, 0, 0),
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new BoxView
                    {
                        WidthRequest = 1,
                        HeightRequest = 18,
                        Color = PaperResources.Colour("Hairline"),
                        VerticalOptions = LayoutOptions.Center,
                    },
                    Affix(suffix),
                },
            };
            inside.Children.Add(suffixBlock);
            Grid.SetColumn(suffixBlock, suffixColumn);
        }

        box = new Border
        {
            BackgroundColor = PaperResources.Colour(mono ? "PaperDeep" : "PaperBright"),
            Stroke = PaperResources.Colour("Hairline"),
            StrokeThickness = Storyworthtelling.Maui.DesignTokens.Shape.HairlineDp,
            StrokeShape = new RoundRectangle { CornerRadius = Storyworthtelling.Maui.DesignTokens.Shape.RadiusInput },
            Padding = new Thickness(Storyworthtelling.Maui.DesignTokens.Space.X3, 0),
            HeightRequest = AccountsLayout.FieldHeightDp,
            Content = inside,
        };

        // The identity green while the field is being typed in; the rose of a bad figure wins over it.
        entry.Focused += (_, _) => Paint(focused: true);
        entry.Unfocused += (_, _) => Paint(focused: false);

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
        invalid = !string.IsNullOrEmpty(message);
        errorLine.Text = invalid ? message : string.Empty;
        errorLine.IsVisible = invalid;
        Paint(Entry.IsFocused);
    }

    /// <summary>The required tint: the Accounts identity cloth, faint, over the box's own fill. Off is none.</summary>
    public void SetRequiredTint(bool tinted)
    {
        tintLayer.Color = tinted
            ? LedgerAreas.All[(int)LedgerArea.Accounts].Cloth.WithAlpha((float)MaterialTokens.RequiredTintAlpha)
            : Colors.Transparent;
    }

    private void Paint(bool focused)
    {
        box.Stroke = PaperResources.Colour(invalid ? "ProblemMark" : focused ? "ActionMark" : "Hairline");
    }

    private static Label Affix(string text) => new()
    {
        Text = text,
        FontFamily = "Mono",
        FontSize = PaperResources.Token<double>("MonoSize"),
        TextColor = PaperResources.Colour("InkSoft"),
        VerticalOptions = LayoutOptions.Center,
    };
}
