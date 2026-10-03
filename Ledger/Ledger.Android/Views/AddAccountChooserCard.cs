using Microsoft.Maui.Controls.Shapes;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// Mockup 4, frame 1: the "Add an account" chooser — a centred action card over the haze, the same
/// component <see cref="InstitutionActionCard"/> already is for the reorder card, with two choices
/// instead of a list of moves. Each choice says what it costs (D21) before the owner taps, not after —
/// "Connect a bank" names the Admin-mode cost right there in its subtitle.
///
/// <para>Not built as a second overload on <see cref="InstitutionActionCard"/>: that component's rows
/// are single-line titles with no subtitle, and retrofitting one would change its shape for a caller
/// (the reorder card) this task does not touch.</para>
/// </summary>
public static class AddAccountChooserCard
{
    public static View Build(Action onConnectBank, Action onAddByHand, Action onDismiss)
    {
        var rows = new VerticalStackLayout();

        rows.Children.Add(BuildHeader());
        rows.Children.Add(BuildRow(
            "Connect a bank",
            "Sign in through Plaid. On this phone, needs Admin mode.",
            () => { onDismiss(); onConnectBank(); }));
        rows.Children.Add(BuildRow(
            "Add one by hand",
            "For a loan or card Plaid can't reach.",
            () => { onDismiss(); onAddByHand(); }));

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

    private static View BuildHeader() => new VerticalStackLayout
    {
        Padding = new Thickness(18, 14, 18, 11),
        Children = { new Label { Text = "Add an account", Style = PaperResources.Token<Style>("HeadingLabel") } },
    };

    private static View BuildRow(string title, string subtitle, Action onTap)
    {
        var text = new VerticalStackLayout
        {
            Spacing = 2,
            Padding = new Thickness(18, 13),
            Children =
            {
                new Label { Text = title, Style = PaperResources.Token<Style>("CardRowTitle") },
                new Label { Text = subtitle, Style = PaperResources.Token<Style>("SecondaryLabel") },
            },
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTap();
        text.GestureRecognizers.Add(tap);

        return new VerticalStackLayout
        {
            Children =
            {
                new BoxView { HeightRequest = 1, Color = PaperResources.Colour("PaperDeep") },
                text,
            },
        };
    }
}
