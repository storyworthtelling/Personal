using Folio.Domain.Shelf;
using Storyworthtelling.Maui;

namespace Folio.Android.Pages;

/// <summary>
/// The library, spine-out: what has been reviewed and put away.
///
/// <para>Series first, each bracketed by bookends on a shelf of its own with a quiet label beneath;
/// then the standalone books. Spine width is word count and nothing else is data — see
/// <see cref="SpineSizing"/>.</para>
/// </summary>
public class ShelfPage : FolioBinderPage
{
    private readonly VerticalStackLayout shelves = new() { Spacing = DesignTokens.Space.X8 };

    public ShelfPage()
    {
        Content = new ScrollView
        {
            Padding = new Thickness(
                DesignTokens.Space.X6, DesignTokens.Space.X2, DesignTokens.Space.X4, DesignTokens.Space.X6),
            Content = shelves,
        };

        Wrap(FolioArea.Shelf, "Shelf");
    }

    /// <summary>
    /// Paints the library. Takes the composed rows rather than the books so the dealing rule — a
    /// series always gets a shelf to itself — stays in <see cref="ShelfComposer"/>, where it is
    /// tested, instead of being re-decided by a screen.
    /// </summary>
    public void Show(IReadOnlyList<ShelfRow> rows)
    {
        shelves.Clear();

        if (rows.Count == 0)
        {
            shelves.Add(new Label
            {
                Text = "Nothing shelved yet. A book arrives here once its review is finished.",
                Style = PaperResources.Token<Style>("SecondaryLabel"),
            });
            return;
        }

        foreach (var row in rows)
        {
            var drawable = new ShelfDrawable { Row = row };

            shelves.Add(new GraphicsView
            {
                Drawable = drawable,
                HeightRequest = drawable.HeightDp,
                HorizontalOptions = LayoutOptions.Fill,
            });

            if (row.SeriesName is not null)
            {
                shelves.Add(new Label
                {
                    Text = $"{row.SeriesName} · {Count(row.Books.Count)}",
                    Style = PaperResources.Token<Style>("SecondaryLabel"),
                    Margin = new Thickness(DesignTokens.Space.X4, DesignTokens.Space.X1, 0, 0),
                });
            }
        }
    }

    // "three books", not "3 books" — the frame spells the small numbers out.
    private static string Count(int books) => books switch
    {
        1 => "one book",
        2 => "two books",
        3 => "three books",
        4 => "four books",
        5 => "five books",
        _ => $"{books} books",
    };
}
