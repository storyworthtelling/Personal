using Microsoft.Maui.Controls.Shapes;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// D123's long-press action card (mockup 2, frame 2): a centred card naming the bank and its position
/// ("4 accounts · first on the page"), then Move up / Move down / Move to the bottom / Disconnect —
/// no drag, the same idiom Snowball's stepper uses (D43). No such component exists yet in
/// <c>Storyworthtelling.Maui</c>, so this is scoped to <c>Ledger.Android</c> alone per this task's
/// brief, matching the mockup's drawn anatomy (title, subtitle, then rows, the disabled row dimmed
/// rather than hidden) rather than inventing a different shape.
///
/// <para>A plain overlay <see cref="View"/> the caller adds to its own root layout and removes again
/// on dismiss — this file holds no navigation or modal-page concept, since <see cref="MainPage"/>'s
/// root is already a <see cref="Grid"/> a translucent scrim can sit over.</para>
/// </summary>
public static class InstitutionActionCard
{
    public static View Build(
        string institutionName,
        string subtitle,
        bool canMoveUp,
        bool canMoveDown,
        Action onMoveUp,
        Action onMoveDown,
        Action onMoveToBottom,
        Action onDisconnect,
        Action onDismiss,
        bool canMoveToBottom = true)
    {
        var rows = new VerticalStackLayout();

        rows.Children.Add(BuildHeader(institutionName, subtitle));
        rows.Children.Add(BuildRow("Move up", () => { onDismiss(); onMoveUp(); }, enabled: canMoveUp));
        rows.Children.Add(BuildRow("Move down", () => { onDismiss(); onMoveDown(); }, enabled: canMoveDown));
        rows.Children.Add(BuildRow("Move to the bottom", () => { onDismiss(); onMoveToBottom(); }, enabled: canMoveToBottom));
        rows.Children.Add(BuildRow($"Disconnect {institutionName}", () => { onDismiss(); onDisconnect(); }));

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

    private static View BuildHeader(string institutionName, string subtitle) => new VerticalStackLayout
    {
        Padding = new Thickness(18, 14, 18, 11),
        Children =
        {
            new Label { Text = institutionName, Style = PaperResources.Token<Style>("HeadingLabel") },
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
