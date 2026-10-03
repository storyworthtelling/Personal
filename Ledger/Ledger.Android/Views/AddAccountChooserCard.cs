using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// Mockup 4, frame 1: the "Add an account" chooser — a centred action card over the paper-deep haze
/// (<see cref="ActionCardHaze"/>, the same setting <see cref="InstitutionActionCard"/> uses for the reorder
/// card), with two choices instead of a list of moves. Each choice carries its icon and says what it costs
/// (D21) before the owner taps, not after — "Connect a bank" names the Admin-mode cost right there in its
/// subtitle, and, as the primary choice, is set in semibold green (<c>AccentDeep</c>).
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
            ActionIconKind.Bank,
            "Connect a bank",
            "Sign in through Plaid. On this phone, needs Admin mode.",
            primary: true,
            () => { onDismiss(); onConnectBank(); }));
        rows.Children.Add(BuildRow(
            ActionIconKind.Pencil,
            "Add one by hand",
            "For a loan or card Plaid can’t reach.",
            primary: false,
            () => { onDismiss(); onAddByHand(); }));

        return ActionCardHaze.Build(rows, onDismiss);
    }

    private static View BuildHeader() => new VerticalStackLayout
    {
        Padding = new Thickness(18, 14, 18, 11),
        Children = { new Label { Text = "Add an account", Style = PaperResources.Token<Style>("HeadingLabel") } },
    };

    private static View BuildRow(ActionIconKind icon, string title, string subtitle, bool primary, Action onTap)
    {
        var text = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label
                {
                    Text = title,
                    Style = PaperResources.Token<Style>("CardRowTitle"),
                    // Semibold throughout; the primary choice is also the deep green (mockup 4).
                    FontFamily = "SansSemibold",
                    TextColor = PaperResources.Colour(primary ? "AccentDeep" : "Ink"),
                },
                new Label
                {
                    Text = subtitle,
                    Style = PaperResources.Token<Style>("SecondaryLabel"),
                    FontSize = PaperResources.Token<double>("LabelSize"),
                    TextColor = PaperResources.Colour("InkSoft"),
                },
            },
        };

        return ActionCardHaze.Row(icon, text, onTap);
    }
}
