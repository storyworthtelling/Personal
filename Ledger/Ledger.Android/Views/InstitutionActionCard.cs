using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// D123's action card (mockup 2, frame 2): a centred card, set over the paper-deep haze, naming the bank and its position
/// ("4 accounts · first on the page"), then Move up / Move down / Move to the bottom / Disconnect —
/// no drag, the same idiom Snowball's stepper uses (D43). No such component exists yet in
/// <c>Storyworthtelling.Maui</c>, so this is scoped to <c>Ledger.Android</c> alone per this task's
/// brief, matching the mockup's drawn anatomy (title, subtitle, then rows, the disabled row dimmed
/// rather than hidden) rather than inventing a different shape.
///
/// <para>A plain overlay <see cref="View"/> the caller hands to its frame's <c>ShowOverlay</c> (and takes
/// away with <c>HideOverlay</c>) — this file holds no navigation or modal-page concept. The haze it returns
/// covers the whole page, binder strip and pinned bar included, as mockup 2 draws.</para>
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
        rows.Children.Add(BuildRow(ActionIconKind.MoveUp, "Move up", () => { onDismiss(); onMoveUp(); }, enabled: canMoveUp));
        rows.Children.Add(BuildRow(ActionIconKind.MoveDown, "Move down", () => { onDismiss(); onMoveDown(); }, enabled: canMoveDown));
        rows.Children.Add(BuildRow(ActionIconKind.MoveToBottom, "Move to the bottom", () => { onDismiss(); onMoveToBottom(); }, enabled: canMoveToBottom));
        rows.Children.Add(BuildRow(ActionIconKind.Disconnect, $"Disconnect {institutionName}", () => { onDismiss(); onDisconnect(); }));

        return ActionCardHaze.Build(rows, onDismiss);
    }

    private static View BuildHeader(string institutionName, string subtitle) => new VerticalStackLayout
    {
        Padding = new Thickness(18, 14, 18, 11),
        Children =
        {
            new Label { Text = institutionName, Style = PaperResources.Token<Style>("HeadingLabel") },
            new Label
            {
                Text = subtitle,
                Style = PaperResources.Token<Style>("SecondaryLabel"),
                FontSize = PaperResources.Token<double>("LabelSize"),
            },
        },
    };

    private static View BuildRow(ActionIconKind icon, string text, Action onTap, bool enabled = true)
    {
        var label = new Label
        {
            Text = text,
            Style = PaperResources.Token<Style>("CardRowTitle"),
            // A row that does nothing is dimmed (mockup 2's `.act.dim`: muted ink at 55%) and, in the row
            // helper, untappable — "if (enabled)" there is what keeps a dimmed Move up from doing nothing silently.
            TextColor = PaperResources.Colour(enabled ? "Ink" : "InkMuted"),
        };

        return ActionCardHaze.Row(icon, label, onTap, enabled, PaperResources.Colour(enabled ? "Ink" : "InkMuted"));
    }
}
