using Microsoft.Maui.Controls.Shapes;
using Index.Domain.Design;
using Storyworthtelling.Maui;

namespace Index.Android.Views;

/// <summary>
/// The setting every action card on Accounts shares (mockups 2 and 4): the page's <c>PaperDeep</c> at 86%
/// laid over the whole page — a warm haze, not a black scrim — with the card centred in the well, 24 from the
/// left and clear of the binder on the right. Not a dialog: tapping anywhere outside the card dismisses it.
///
/// <para>The haze is built to be handed to <c>LedgerBinderPage.ShowOverlay</c>, which spans the header, the
/// well, the binder strip and the pinned bar, so all of them fade behind it as the mockups draw.</para>
/// </summary>
public static class ActionCardHaze
{
    /// <param name="rows">The card's content: a header and its rows.</param>
    public static View Build(View rows, Action onDismiss)
    {
        var card = new Border
        {
            BackgroundColor = Color.FromArgb(MaterialTokens.NoteCardHex),
            Stroke = PaperResources.Colour("Hairline"),
            StrokeThickness = Storyworthtelling.Maui.DesignTokens.Shape.HairlineDp,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(Storyworthtelling.Maui.DesignTokens.Shape.RadiusCard),
            },
            Margin = new Thickness(AccountsLayout.ActionCardLeftDp, 0, AccountsLayout.ActionCardRightDp, 0),
            VerticalOptions = LayoutOptions.Center,
            Content = rows,
        };

        // A tap on the card itself is not "outside it": swallow it so it never reaches the haze below.
        card.GestureRecognizers.Add(new TapGestureRecognizer());

        var haze = new Grid
        {
            BackgroundColor = PaperResources.Colour("PaperDeep").WithAlpha((float)MaterialTokens.HazeAlpha),
            Children = { card },
        };
        var dismissTap = new TapGestureRecognizer();
        dismissTap.Tapped += (_, _) => onDismiss();
        haze.GestureRecognizers.Add(dismissTap);

        return haze;
    }

    /// <summary>One row of an action card (mockup 2/4's <c>.act</c>): a rule above, the icon, then the text,
    /// at least 48dp tall. <paramref name="content"/> is the row's text block.</summary>
    public static View Row(ActionIconKind icon, View content, Action? onTap, bool enabled = true, Color? iconInk = null)
    {
        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 11,
            Padding = new Thickness(18, 13),
            MinimumHeightRequest = Storyworthtelling.Maui.DesignTokens.Shape.TouchTargetDp,
            Opacity = enabled ? 1.0 : 0.55,
        };
        var glyph = new ActionIcon(icon, iconInk);
        glyph.Margin = new Thickness(0, 2, 0, 0);
        row.Children.Add(glyph);
        Grid.SetColumn(glyph, 0);
        content.VerticalOptions = LayoutOptions.Center;
        row.Children.Add(content);
        Grid.SetColumn(content, 1);

        if (enabled && onTap is not null)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => onTap();
            row.GestureRecognizers.Add(tap);
        }

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
