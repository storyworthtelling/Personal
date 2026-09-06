namespace Storyworthtelling.Maui.Views;

/// <param name="ArtworkPath">
/// A local cover file, or null. Present only where the art distinguishes one row from the next
/// (DESIGN.md §7 Artwork slot) — a song list, not an album's tracks.
/// </param>
/// <param name="Picked">
/// Whether this row is chosen, while the list is in selection mode. Ignored otherwise — a list that
/// is not picking has no picked rows to show.
/// </param>
public sealed record PaperListRow(
    string Primary, string? Secondary = null, string? ArtworkPath = null, bool Picked = false);

/// <summary>
/// The DESIGN.md list-row recipe for MAUI: 56dp minimum height, Sans primary line in ink, Sans
/// secondary line in muted ink, hairline dividers between.
///
/// <para>In selection mode the leading slot becomes a checkbox and cover art comes <b>off</b>. The
/// art is dropped rather than squeezed beside the box — at this row height both crush the title,
/// and while you are picking, the title is the thing you are reading.</para>
/// </summary>
public static class PaperList
{
    /// <summary>The divider between rows. A <see cref="CollectionView"/> draws none of its own.</summary>
    public static BoxView Divider() => new()
    {
        HeightRequest = DesignTokens.Shape.HairlineDp,
        Color = PaperResources.Colour("Hairline"),
    };

    /// <summary>
    /// One row. Built rather than templated so a caller can place a row outside a
    /// <see cref="CollectionView"/> — a settings card holding three of them is still a list.
    /// </summary>
    public static View Row(PaperListRow row, bool picking = false)
    {
        var lines = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = row.Primary, Style = PaperResources.Token<Style>("CardRowTitle") },
            },
        };

        if (row.Secondary is not null)
        {
            lines.Children.Add(new Label { Text = row.Secondary, Style = PaperResources.Token<Style>("SecondaryLabel") });
        }

        var content = new Grid
        {
            ColumnSpacing = DesignTokens.Space.X3,
            MinimumHeightRequest = DesignTokens.Shape.ListRowMinHeightDp,
            Padding = new Thickness(DesignTokens.Space.X4, DesignTokens.Space.X3),

            // §8 Selected: the highlighter wash, behind full-strength ink. Set both ways rather
            // than only when picked — a recycled row that is not picked has to actively say so or
            // it inherits the last one's wash.
            BackgroundColor = picking && row.Picked ? PaperResources.Colour("ActionWash") : Colors.Transparent,
        };

        // Built with the parameterless ctor and Width rather than ColumnDefinitions = [..]: the
        // collection's initializer form is not something to rely on from memory in a project whose
        // only compiler is CI.
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        var leading = Leading(row, picking);
        if (leading is not null)
        {
            Grid.SetColumn(leading, 0);
            content.Children.Add(leading);
        }

        Grid.SetColumn(lines, 1);
        content.Children.Add(lines);
        return content;
    }

    private static View? Leading(PaperListRow row, bool picking)
    {
        if (picking)
        {
            return new GraphicsView
            {
                Drawable = new CheckboxDrawable(PaperResources.Colour) { Picked = row.Picked },
                WidthRequest = DesignTokens.Shape.CheckboxDp,
                HeightRequest = DesignTokens.Shape.CheckboxDp,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
            };
        }

        if (row.ArtworkPath is null)
        {
            return null;
        }

        return new Image
        {
            Source = ImageSource.FromFile(row.ArtworkPath),
            WidthRequest = ArtworkSizeDp,
            HeightRequest = ArtworkSizeDp,
            Aspect = Aspect.AspectFill,
            VerticalOptions = LayoutOptions.Center,
        };
    }

    /// <summary>The cover's size in a row — square, and small enough not to set the row height.</summary>
    public const int ArtworkSizeDp = 40;

}
