using Folio.Domain.Readiness;
using Storyworthtelling.Maui;

namespace Folio.Android.Pages;

/// <summary>One draft in the list under the chart.</summary>
public sealed record DraftRow(int Number, string Date, string Words);

/// <summary>What the book page needs to draw itself.</summary>
public sealed record BookPageModel(
    string Title,
    ImageSource? Jacket,
    string Standing,
    string Drafts,
    string Measure,
    IReadOnlyList<DraftFindings> Readiness,
    IReadOnlyList<DraftRow> Rows);

/// <summary>
/// One book: what it is, how ready it is, and its drafts.
///
/// <para><b>The ground is paper here.</b> Only the Work desk is wood — the desk is where books are
/// being handled, and this is a record of one.</para>
///
/// <para><b>Readiness is counts, never a score.</b> Musts falling and Greats rising is what getting
/// better looks like, drawn from findings the report already produced, so nothing about the book is
/// ever compressed into a number.</para>
///
/// <para>The bar carries the primary and one secondary that §7 allows: <b>Open report</b>, which is
/// what closes the risk that dropping the in-app reader left the product with no way to reach its
/// own output, and <b>New draft</b> beside it.</para>
/// </summary>
public class BookPage : FolioBinderPage
{
    private static readonly IReadOnlyDictionary<ReadinessTier, string> TierNames =
        new Dictionary<ReadinessTier, string>
        {
            [ReadinessTier.Must] = "Must",
            [ReadinessTier.Should] = "Should",
            [ReadinessTier.Consider] = "Consider",
            [ReadinessTier.Great] = "Great",
        };

    private readonly VerticalStackLayout body = new();

    public BookPage(BookPageModel book)
    {
        Content = new ScrollView
        {
            Padding = new Thickness(24, 4, 16, 24),
            Content = body,
        };

        Wrap(FolioArea.Shelf, book.Title);
        Show(book);

        SetBar(
            new Button { Text = "New draft", Style = PaperResources.Token<Style>("SecondaryButton") },
            new Button { Text = "Open report" });
    }

    private void Show(BookPageModel book)
    {
        body.Add(Hero(book));

        body.Add(SectionLabel("Readiness"));
        body.Add(Chart(book.Readiness));
        body.Add(Legend(book.Readiness));

        body.Add(SectionLabel("Drafts"));
        foreach (var row in book.Rows)
        {
            body.Add(Row(row, last: ReferenceEquals(row, book.Rows[^1])));
        }
    }

    // The volume itself, cover-out, with what is known about it beside it.
    private static View Hero(BookPageModel book)
    {
        var row = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 16,
        };

        var cover = new FlatBook(book.Title, book.Jacket, 104, 130, tilt: 0)
        {
            VerticalOptions = LayoutOptions.Start,
        };

        var meta = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Start,
            Children =
            {
                new Label { Text = book.Standing, FontFamily = "SerifSemibold", FontSize = 18, LineHeight = 1.2 },
                new Label
                {
                    Text = book.Drafts,
                    FontSize = 14,
                    TextColor = PaperResources.Colour("InkMuted"),
                    Margin = new Thickness(0, 3, 0, 0),
                },
                new Label
                {
                    Text = book.Measure,
                    FontSize = 14,
                    TextColor = PaperResources.Colour("InkSoft"),
                    Margin = new Thickness(0, 8, 0, 0),
                },
            },
        };

        row.Children.Add(cover);
        row.Children.Add(meta);
        Grid.SetColumn(cover, 0);
        Grid.SetColumn(meta, 1);
        return row;
    }

    private static View Chart(IReadOnlyList<DraftFindings> readiness) => new GraphicsView
    {
        Drawable = new ReadinessChartDrawable
        {
            Plot = ReadinessChart.Plot(readiness),
            Labels = TierNames.ToDictionary(
                entry => entry.Key,
                entry => $"{entry.Value} {Latest(readiness, entry.Key)}"),
        },
        HeightRequest = ReadinessChartDrawable.FrameHeight,
        Margin = new Thickness(0, 10, 0, 0),
        InputTransparent = true,
    };

    // The name at a line's end carries its latest count, as the frame prints it: "Must 5".
    private static int Latest(IReadOnlyList<DraftFindings> readiness, ReadinessTier tier) =>
        readiness.Count > 0 && readiness[^1].Counts.TryGetValue(tier, out var count) ? count : 0;

    /// <summary>
    /// A legend as well as the direct labels. §17 exempts this single-owner suite from colourblind
    /// redundancy on screen; both are here anyway, because a name beside a line costs nothing and
    /// survives any vision.
    /// </summary>
    private static View Legend(IReadOnlyList<DraftFindings> readiness)
    {
        var row = new HorizontalStackLayout { Spacing = 12, Margin = new Thickness(0, 6, 0, 0) };

        foreach (var tier in Enum.GetValues<ReadinessTier>())
        {
            if (!readiness.Any(draft => draft.Counts.ContainsKey(tier)))
            {
                continue;
            }

            row.Children.Add(new HorizontalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    new Border
                    {
                        WidthRequest = 9,
                        HeightRequest = 9,
                        StrokeThickness = 0,
                        BackgroundColor = ReadinessChartDrawable.InkFor(tier),
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 },
                        VerticalOptions = LayoutOptions.Center,
                    },
                    new Label
                    {
                        Text = TierNames[tier],
                        FontSize = 11.5,
                        TextColor = PaperResources.Colour("InkSoft"),
                        VerticalOptions = LayoutOptions.Center,
                    },
                },
            });
        }

        return row;
    }

    private static View SectionLabel(string text) => new Label
    {
        Text = text.ToUpperInvariant(),
        FontFamily = "SansSemibold",
        FontSize = 13,
        CharacterSpacing = 0.1,
        TextColor = PaperResources.Colour("InkMuted"),
        Margin = new Thickness(0, 20, 0, 8),
    };

    // A draft: its number, when it arrived, and how long it was. The count is monospaced and
    // tabular so the column reads down as numbers rather than as words that happen to be digits.
    private static View Row(DraftRow row, bool last)
    {
        var line = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            ],
            ColumnSpacing = 10,
            Padding = new Thickness(0, 12),
        };

        var number = new Label { Text = row.Number.ToString(), FontFamily = "SerifSemibold", FontSize = 17 };
        var date = new Label
        {
            Text = row.Date,
            FontSize = 13,
            TextColor = PaperResources.Colour("InkMuted"),
            VerticalOptions = LayoutOptions.End,
        };
        var words = new Label
        {
            Text = row.Words,
            Style = PaperResources.Token<Style>("MonoLabel"),
            FontSize = 13,
            TextColor = PaperResources.Colour("InkMuted"),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
        };

        line.Children.Add(number);
        line.Children.Add(date);
        line.Children.Add(words);
        Grid.SetColumn(number, 0);
        Grid.SetColumn(date, 1);
        Grid.SetColumn(words, 2);

        if (last)
        {
            return line;
        }

        return new VerticalStackLayout
        {
            Children =
            {
                line,
                new BoxView
                {
                    HeightRequest = DesignTokens.Shape.HairlineDp,
                    Color = PaperResources.Colour("Hairline"),
                },
            },
        };
    }
}
