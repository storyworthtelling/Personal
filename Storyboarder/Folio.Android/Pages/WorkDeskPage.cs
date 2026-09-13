using Storyworthtelling.Maui;
using Storyworthtelling.Paper;

namespace Folio.Android.Pages;

/// <summary>The book being read now, and how far through it the readers are.</summary>
/// <param name="Fraction">How much of the pass is done, 0 to 1. Null hides the meter.</param>
public sealed record ReadingNow(
    string Title,
    ImageSource? Jacket,
    string Draft,
    string Words,
    string Status,
    double? Fraction,
    string Section);

/// <summary>One of the books waiting on the desk, and what its post-it says.</summary>
/// <param name="Ink">The role its state is written in — a stall is a problem, ready is positive.</param>
public sealed record DeskBook(string Title, ImageSource? Jacket, string Draft, string State, Color Ink);

/// <summary>
/// Work — the desk, seen from above, and the app's home.
///
/// <para>The wood is the ground, so the books lie flat and their covers face up: the one composition
/// where the painted jackets are the main thing you see. Every small label lies on paper — section
/// labels on cream strips, a book's details on a note card, a passing state on a post-it. That is
/// not decoration. Muted ink on this wood is 1.40:1; on the note card it is the value it is on every
/// other screen in the suite (see <c>MaterialContrastTests</c>).</para>
/// </summary>
public class WorkDeskPage : FolioBinderPage
{
    private readonly VerticalStackLayout desk = new();

    public WorkDeskPage()
    {
        Content = new ScrollView
        {
            Padding = new Thickness(24, 4, 16, 24),
            Content = desk,
        };

        SetBackdrop(new GraphicsView { Drawable = new DeskDrawable() });
        Wrap(FolioArea.Work, "Work desk");
    }

    /// <param name="voice">
    /// The owner's line, in their own hand. Empty hides it rather than printing a neutral stand-in —
    /// the voice is either theirs or it is absent.
    /// </param>
    public void Show(ReadingNow? reading, string? voice, IReadOnlyList<DeskBook> waiting)
    {
        desk.Clear();

        if (reading is not null)
        {
            desk.Add(DeskPaper.Strip("Reading now"));
            desk.Add(Hero(reading));
        }

        if (!string.IsNullOrWhiteSpace(voice))
        {
            desk.Add(new Label
            {
                Text = voice,
                Style = PaperResources.Token<Style>("VoiceLabel"),
                Margin = new Thickness(2, 16, 2, 0),
            });
        }

        if (waiting.Count > 0)
        {
            desk.Add(new BoxView { HeightRequest = 16, Color = Colors.Transparent });
            desk.Add(DeskPaper.Strip("Also on the desk"));
            desk.Add(Waiting(waiting));
        }

        // The desk goes on past the last book, said with two pencil marks rather than empty wood.
        desk.Add(new GraphicsView
        {
            Drawable = new DeskFillerDrawable(),
            HeightRequest = 150,
            Margin = new Thickness(0, 10, 0, 0),
            InputTransparent = true,
        });
    }

    // The book being read, with the note card beside it saying how far through it is.
    private static View Hero(ReadingNow reading)
    {
        var progress = PaperResources.Colour("InkBlue");
        var lines = new List<View>
        {
            new Label { Text = reading.Draft, FontFamily = "SerifSemibold", FontSize = 17, LineHeight = 1.15 },
            new Label
            {
                Text = reading.Words,
                FontSize = 13,
                TextColor = PaperResources.Colour("InkSoft"),
                Margin = new Thickness(0, 2, 0, 0),
            },
            new Label
            {
                Text = reading.Status,
                FontSize = 14,
                TextColor = progress,
                Margin = new Thickness(0, 12, 0, 0),
            },
        };

        if (reading.Fraction is { } fraction)
        {
            lines.Add(Meter(fraction, progress));
        }

        lines.Add(new Label
        {
            Text = reading.Section,
            FontSize = 13,
            TextColor = PaperResources.Colour("InkSoft"),
            Margin = new Thickness(0, 6, 0, 0),
        });

        var card = DeskPaper.Card([.. lines]);
        card.Rotation = 0.8;
        card.HorizontalOptions = LayoutOptions.Fill;
        card.VerticalOptions = LayoutOptions.Start;

        var row = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 14,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var cover = new FlatBook(reading.Title, reading.Jacket, 118, 148, tilt: -1.6)
        {
            VerticalOptions = LayoutOptions.Start,
        };
        row.Children.Add(cover);
        row.Children.Add(card);
        Grid.SetColumn(cover, 0);
        Grid.SetColumn(card, 1);
        return row;
    }

    /// <summary>
    /// How far through the pass is — a filled bar, not a spinner (§8: loading is a status sentence
    /// and a measure, never an animation that says only "something is happening").
    ///
    /// <para>Proportioned by star-sizing rather than by setting a width from the fraction: a width in
    /// dp needs the parent to have been measured, and the parent has not been when this is built.</para>
    /// </summary>
    private static View Meter(double fraction, Color ink)
    {
        var filled = Math.Clamp(fraction, 0, 1);

        var bar = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(new GridLength(filled, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1 - filled, GridUnitType.Star)),
            ],
        };

        var done = new BoxView { Color = ink };
        bar.Children.Add(done);
        Grid.SetColumn(done, 0);

        return new Border
        {
            HeightRequest = 5,
            Margin = new Thickness(0, 10, 0, 0),
            StrokeThickness = 0,
            BackgroundColor = PaperResources.Colour("PaperShade"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 3 },
            Content = bar,
        };
    }

    // The rest of the desk: a smaller cover with its state on a post-it under it. The tilts are
    // fixed rather than random, so the desk looks the same each time it is opened.
    private static View Waiting(IReadOnlyList<DeskBook> books)
    {
        var tilts = new[] { 1.4, -2.2, 0.9 };
        var noteTilts = new[] { -1.8, 1.2, -0.9 };

        var row = new HorizontalStackLayout { Spacing = 11, Margin = new Thickness(0, 10, 0, 0) };

        for (var i = 0; i < books.Count; i++)
        {
            var book = books[i];
            var note = DeskPaper.PostIt(book.Draft, book.State, book.Ink);
            note.Rotation = noteTilts[i % noteTilts.Length];

            row.Children.Add(new VerticalStackLayout
            {
                WidthRequest = 88,
                Spacing = 7,
                Children =
                {
                    new FlatBook(book.Title, book.Jacket, 88, 110, tilts[i % tilts.Length]),
                    note,
                },
            });
        }

        return row;
    }
}

