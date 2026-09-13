using Microsoft.Maui.Controls.Shapes;
using Storyworthtelling.Maui;
using Storyworthtelling.Paper;

namespace Folio.Android.Pages;

/// <summary>How an action reads on the card.</summary>
public enum ActionWeight
{
    /// <summary>The ordinary case: ink, regular weight.</summary>
    Normal,

    /// <summary>The one thing the card is mostly for. Action pine, and it carries weight.</summary>
    Primary,

    /// <summary>Available, not offered. Muted ink, no colour, and it sits last.</summary>
    Quiet,
}

/// <summary>One row on the card. <paramref name="Glyph"/> is 18-unit path data.</summary>
public sealed record CardAction(string Label, string Glyph, ActionWeight Weight, Action Chosen);

/// <summary>
/// The §7 action card: what a long-press offers.
///
/// <para><b>It is a card, not a dialog.</b> Paper centred over a haze of the page's own deep tone,
/// carrying only actions for the item that was pressed, dismissed by tapping outside it. The
/// distinction is not cosmetic — a dialog interrupts and demands an answer, and this offers a set of
/// things you might do and takes no for an answer by being tapped past.</para>
///
/// <para><b>Nothing here asks "are you sure".</b> Archiving is prominent and deleting is merely
/// available: <i>Put on the shelf</i> is the single row in Action pine, and <i>Delete</i> sits last
/// in muted ink with no colour and no confirmation — because destructive acts are undo-first (§6),
/// and the undo is the dog-ear, not a question asked in advance.</para>
/// </summary>
public static class ActionCard
{
    /// <summary>The glyph viewport the rows are drawn in.</summary>
    public const float GlyphViewport = 18f;

    /// <summary>
    /// Builds the overlay. Add it to a page's root grid to present it; the returned view removes
    /// itself when it is dismissed.
    /// </summary>
    public static View Build(string title, string subtitle, IReadOnlyList<CardAction> actions, Action dismissed)
    {
        var overlay = new Grid();

        var haze = new BoxView
        {
            Color = PaperResources.Colour("PaperDeep").WithAlpha(0.86f),
        };
        var tapOut = new TapGestureRecognizer();
        tapOut.Tapped += (_, _) => dismissed();
        haze.GestureRecognizers.Add(tapOut);

        var rows = new VerticalStackLayout();
        rows.Add(Head(title, subtitle));

        for (var i = 0; i < actions.Count; i++)
        {
            rows.Add(Row(actions[i], last: i == actions.Count - 1, dismissed));
        }

        var card = new Border
        {
            Margin = new Thickness(24, 0, 24, 0),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromArgb(MaterialTokens.NoteCardHex),
            Stroke = PaperResources.Colour("Hairline"),
            StrokeThickness = DesignTokens.Shape.HairlineDp,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = DesignTokens.Shape.RadiusCard },
            Content = rows,
        };

        overlay.Children.Add(haze);
        overlay.Children.Add(card);
        return overlay;
    }

    private static View Head(string title, string subtitle) => new VerticalStackLayout
    {
        Padding = new Thickness(18, 14, 18, 11),
        Children =
        {
            new Label { Text = title, FontFamily = "SerifSemibold", FontSize = 17, LineHeight = 1.15 },
            new Label
            {
                Text = subtitle,
                FontSize = 12.5,
                TextColor = PaperResources.Colour("InkMuted"),
                Margin = new Thickness(0, 2, 0, 0),
            },
        },
    };

    private static View Row(CardAction action, bool last, Action dismissed)
    {
        var ink = action.Weight switch
        {
            ActionWeight.Primary => PaperResources.Colour("ActionMark"),
            ActionWeight.Quiet => PaperResources.Colour("InkMuted"),
            _ => PaperResources.Colour("Ink"),
        };

        var row = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 11,
            Padding = new Thickness(18, 13),
            // §5: every row clears the touch minimum, whatever its type size.
            MinimumHeightRequest = DesignTokens.Shape.TouchTargetDp,
        };

        var glyph = new GraphicsView
        {
            WidthRequest = GlyphViewport,
            HeightRequest = GlyphViewport,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            Drawable = new CardGlyphDrawable { PathData = action.Glyph, Ink = ink },
        };

        var label = new Label
        {
            Text = action.Label,
            FontSize = 15,
            TextColor = ink,
            FontFamily = action.Weight == ActionWeight.Primary ? "SansSemibold" : null,
            VerticalOptions = LayoutOptions.Center,
        };

        row.Children.Add(glyph);
        row.Children.Add(label);
        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(label, 1);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            dismissed();
            action.Chosen();
        };
        row.GestureRecognizers.Add(tap);

        if (last)
        {
            return row;
        }

        return new VerticalStackLayout
        {
            Children =
            {
                row,
                new BoxView
                {
                    HeightRequest = DesignTokens.Shape.HairlineDp,
                    Color = PaperResources.Colour("PaperDeep"),
                },
            },
        };
    }
}

/// <summary>
/// A row's glyph, stroked from 18-unit path data in whatever ink the row is written in.
///
/// <para>Stroked rather than filled, and path data rather than an image, for the same reason the
/// binder's glyphs are: the same mark is drawn in three inks here — pine on the primary row, ink on
/// the ordinary ones, muted on the quiet one — and an <c>ImageSource</c> cannot be re-tinted.</para>
/// </summary>
public sealed class CardGlyphDrawable : IDrawable
{
    public required string PathData { get; init; }

    public required Color Ink { get; init; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();
        canvas.Scale(dirtyRect.Width / ActionCard.GlyphViewport, dirtyRect.Height / ActionCard.GlyphViewport);
        canvas.StrokeColor = Ink;
        canvas.StrokeSize = 1.7f;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawPath(PathBuilder.Build(PathData));
        canvas.RestoreState();
    }
}

