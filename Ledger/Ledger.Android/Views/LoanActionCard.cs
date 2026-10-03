using Microsoft.Maui.Controls.Shapes;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// D43/D123's long-press action card for a loan (mockup 6: "Reorder by long-pressing a card"): the
/// same anatomy <see cref="InstitutionActionCard"/> already established for a bank's reorder card
/// (title, subtitle, then rows, a scrim behind, tap-outside dismisses) — <b>duplicated rather than
/// reused</b>, because that card's fourth row is <c>Disconnect</c>, which has no equivalent for a
/// loan (Snowball offers no disconnect action at all, per mockup 6's own note: "No pinned bar: the
/// extra payment is a field, not an action"). Widening <see cref="InstitutionActionCard"/> to take an
/// optional fourth action instead of duplicating this file would have been the more DRY move, but it
/// would also have meant editing a file this task's brief did not ask for; this is the smaller, safer
/// change of the two, at the cost of ~50 duplicated lines of chrome.
/// </summary>
public static class LoanActionCard
{
    public static View Build(
        string loanName,
        string subtitle,
        bool canMoveUp,
        bool canMoveDown,
        Action onMoveUp,
        Action onMoveDown,
        Action onMoveToBottom,
        Action onDismiss)
    {
        var rows = new VerticalStackLayout();

        rows.Children.Add(BuildHeader(loanName, subtitle));
        rows.Children.Add(BuildRow("Move up", () => { onDismiss(); onMoveUp(); }, enabled: canMoveUp));
        rows.Children.Add(BuildRow("Move down", () => { onDismiss(); onMoveDown(); }, enabled: canMoveDown));
        rows.Children.Add(BuildRow("Move to the bottom", () => { onDismiss(); onMoveToBottom(); }));

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

    private static View BuildHeader(string loanName, string subtitle) => new VerticalStackLayout
    {
        Padding = new Thickness(18, 14, 18, 11),
        Children =
        {
            new Label { Text = loanName, Style = PaperResources.Token<Style>("HeadingLabel") },
            new Label { Text = subtitle, Style = PaperResources.Token<Style>("SecondaryLabel") },
        },
    };

    private static View BuildRow(string text, Action onTap, bool enabled = true)
    {
        var label = new Label
        {
            Text = text,
            Style = PaperResources.Token<Style>("CardRowTitle"),
            Opacity = enabled ? 1.0 : 0.55,
            Padding = new Thickness(18, 13),
        };

        if (enabled)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => onTap();
            label.GestureRecognizers.Add(tap);
        }

        return new VerticalStackLayout
        {
            Children =
            {
                new BoxView { HeightRequest = 1, Color = PaperResources.Colour("PaperDeep") },
                label,
            },
        };
    }
}
