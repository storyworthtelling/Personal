using Microsoft.Maui.Controls.Shapes;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>One row's worth of what the recategorize card needs to know about a category.</summary>
public sealed record RecategorizeCategoryOption(Guid CategoryId, string Name, bool IsCurrent);

/// <summary>
/// D58's recategorize action card (mockup 7, frame 2): a centred card naming the transaction, then
/// every active category with a check mark on the current one, then — always visible, before any tap,
/// not a follow-up screen — the one sentence saying what picking a different one means. Modeled
/// directly on <see cref="InstitutionActionCard"/>'s own shape (centred card, scrim, dismiss-on-tap-
/// outside) rather than a new pattern, since it is the same "long-press card" component drawn a second
/// time for a different list.
/// </summary>
public static class RecategorizeActionCard
{
    public static View Build(
        string transactionSummary,
        string transactionSubtitle,
        string merchantName,
        IReadOnlyList<RecategorizeCategoryOption> categories,
        Action<Guid> onPick,
        Action onDismiss)
    {
        var rows = new VerticalStackLayout();

        rows.Children.Add(BuildHeader(transactionSummary, transactionSubtitle));

        foreach (var category in categories)
        {
            rows.Children.Add(BuildRow(category, () => { onDismiss(); onPick(category.CategoryId); }));
        }

        // D58/D62: the explanation lives on the card itself, before any pick, not as a confirmation
        // shown afterward — the exact copy the mockup prints.
        rows.Children.Add(new Label
        {
            Text = $"{merchantName} goes to the category you pick from now on. Earlier transactions stay where they are.",
            Style = PaperResources.Token<Style>("SecondaryLabel"),
            Padding = new Thickness(18, 10, 18, 14),
        });

        var card = new Border
        {
            BackgroundColor = PaperResources.Colour("PaperBright"),
            Stroke = PaperResources.Colour("Hairline"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            WidthRequest = 300,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = rows,
        };

        var scrim = new Grid
        {
            BackgroundColor = Colors.Black.WithAlpha(0.35f),
            Children = { card },
        };
        var dismissTap = new TapGestureRecognizer();
        dismissTap.Tapped += (_, _) => onDismiss();
        scrim.GestureRecognizers.Add(dismissTap);

        return scrim;
    }

    private static View BuildHeader(string summary, string subtitle) => new VerticalStackLayout
    {
        Padding = new Thickness(18, 14, 18, 11),
        Children =
        {
            new Label { Text = summary, Style = PaperResources.Token<Style>("HeadingLabel") },
            new Label { Text = subtitle, Style = PaperResources.Token<Style>("SecondaryLabel") },
        },
    };

    private static View BuildRow(RecategorizeCategoryOption category, Action onTap)
    {
        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10,
            Padding = new Thickness(18, 13),
        };

        var check = new Label
        {
            Text = category.IsCurrent ? "✓" : string.Empty,
            Style = PaperResources.Token<Style>("CardRowTitle"),
            WidthRequest = 16,
        };
        row.Children.Add(check);
        Grid.SetColumn(check, 0);

        var name = new Label
        {
            Text = category.Name,
            Style = PaperResources.Token<Style>("CardRowTitle"),
            FontAttributes = category.IsCurrent ? FontAttributes.Bold : FontAttributes.None,
        };
        row.Children.Add(name);
        Grid.SetColumn(name, 1);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTap();
        row.GestureRecognizers.Add(tap);

        return new VerticalStackLayout
        {
            Children =
            {
                new BoxView { HeightRequest = 1, Color = PaperResources.Colour("PaperDeep") },
                row,
            },
        };
    }
}
