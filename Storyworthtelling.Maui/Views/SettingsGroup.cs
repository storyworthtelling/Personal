using Microsoft.Maui.Controls.Shapes;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// One Settings group, the MAUI half of <c>Codex.Ui.SettingsAccordion</c> (audit W8.4b; owner's rulings:
/// folding groups in every app, drawn as Pulp's ruled rows): a Serif heading, its quiet line right-aligned
/// in the muted ink, a chevron, a hairline under the group, and the body opening flat on the page in place —
/// no card, no box. A page lists its groups in the suite's order (<c>Storyworthtelling.Updates.SettingsAnatomy</c>):
/// Connections, Vault, the app's own, Diagnostics, then the build line; <c>SettingsStandardTests</c> reads it.
///
/// <code>
/// &lt;kit:SettingsGroup Title="Vault" Meta="1 key needed"&gt;
///     &lt;VerticalStackLayout&gt; … &lt;/VerticalStackLayout&gt;
/// &lt;/kit:SettingsGroup&gt;
/// </code>
/// </summary>
[ContentProperty(nameof(Body))]
public sealed class SettingsGroup : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(SettingsGroup), string.Empty, propertyChanged: (view, _, _) => ((SettingsGroup)view).Paint());

    public static readonly BindableProperty MetaProperty = BindableProperty.Create(
        nameof(Meta), typeof(string), typeof(SettingsGroup), null, propertyChanged: (view, _, _) => ((SettingsGroup)view).Paint());

    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen), typeof(bool), typeof(SettingsGroup), false, propertyChanged: (view, _, _) => ((SettingsGroup)view).Paint());

    public static readonly BindableProperty BodyProperty = BindableProperty.Create(
        nameof(Body), typeof(View), typeof(SettingsGroup), null, propertyChanged: (view, _, _) => ((SettingsGroup)view).Paint());

    private readonly Label heading = new() { Style = PaperResources.Token<Style>("HeadingLabel"), VerticalOptions = LayoutOptions.Center };

    private readonly Label meta = new()
    {
        Style = PaperResources.Token<Style>("SecondaryLabel"),
        VerticalOptions = LayoutOptions.Center,
        LineBreakMode = LineBreakMode.TailTruncation,
        MaxLines = 1,
        Margin = new Thickness(DesignTokens.Space.X3, 0, 0, 0),
    };

    // The fold's chevron in the muted ink: down when closed, turned up when open.
    private readonly Polyline chevron = new()
    {
        Points = [new Point(4.5, 6.4), new Point(8, 9.9), new Point(11.5, 6.4)],
        Stroke = PaperResources.Colour("InkMuted"),
        StrokeThickness = 2,
        StrokeLineCap = PenLineCap.Round,
        WidthRequest = DesignTokens.Space.X4,
        HeightRequest = DesignTokens.Space.X4,
        VerticalOptions = LayoutOptions.Center,
        Margin = new Thickness(DesignTokens.Space.X2, 0, 0, 0),
    };

    private readonly ContentView bodySlot = new() { Padding = new Thickness(0, 0, 0, DesignTokens.Space.X3) };

    public SettingsGroup()
    {
        var header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
            MinimumHeightRequest = DesignTokens.Shape.ListRowMinHeightDp,
        };
        header.Add(heading, 0);
        header.Add(meta, 1);
        header.Add(chevron, 2);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => IsOpen = !IsOpen;
        header.GestureRecognizers.Add(tap);

        Content = new VerticalStackLayout
        {
            Children =
            {
                header,
                bodySlot,
                new BoxView { HeightRequest = DesignTokens.Shape.HairlineDp, Color = PaperResources.Colour("Hairline") },
            },
        };
        Paint();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The quiet line beside the heading, or nothing (a working key has no line).</summary>
    public string? Meta
    {
        get => (string?)GetValue(MetaProperty);
        set => SetValue(MetaProperty, value);
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>What the group holds once open.</summary>
    public View? Body
    {
        get => (View?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    private void Paint()
    {
        heading.Text = Title;
        meta.Text = Meta;
        meta.IsVisible = !string.IsNullOrWhiteSpace(Meta);
        chevron.Rotation = IsOpen ? 180 : 0;
        bodySlot.Content = Body;
        bodySlot.IsVisible = IsOpen && Body is not null;
        SemanticProperties.SetDescription(this, $"{Title}{(string.IsNullOrWhiteSpace(Meta) ? string.Empty : ", " + Meta)}, {(IsOpen ? "open" : "closed")}");
    }
}
