using Microsoft.Extensions.DependencyInjection;
using Folio.Domain.Shelf;
using Folio.Infrastructure.Services;
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

        // The shelf carries the same primary the desk does, as the drawing has it. A book arrives
        // here by being finished and put away, so this is not where one is added — but a screen in
        // the binder with no pinned bar is the one screen where §7's primary is nowhere, and the
        // owner who has just looked at their library is exactly who reaches for it.
        var add = new Button { Text = "Add manuscript" };
        add.Clicked += (_, _) => _ = AddManuscript();
        SetBar(add);
    }

    private async Task AddManuscript() =>
        await Navigation.PushAsync(
            IPlatformApplication.Current!.Services.GetRequiredService<NewProjectPage>());

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadAsync();
    }

    /// <summary>
    /// Reads what has been put away and deals it onto shelves.
    ///
    /// <para>The dealing itself is <see cref="ShelfComposer"/> — a series gets a shelf to itself —
    /// so the rule stays where it is tested rather than being re-decided by a screen.</para>
    /// </summary>
    private async Task LoadAsync()
    {
        IReadOnlyList<NovelStatus> shelved;
        try
        {
            using var scope = IPlatformApplication.Current!.Services.CreateScope();
            shelved = await scope.ServiceProvider.GetRequiredService<ProjectService>()
                .ListShelvedAsync(CancellationToken.None);
        }
        catch
        {
            // A transient read should not empty the shelf on screen; the next appearance retries.
            return;
        }

        Show(ShelfComposer.Compose(
        [
            .. shelved.Select(book => new ShelfBook(
                book.Id,
                book.Title,
                book.SeriesId?.ToString(),
                book.SeriesSequence,
                book.WordCount,
                book.SeriesName)),
        ]));
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

            var shelf = new GraphicsView
            {
                Drawable = drawable,
                HeightRequest = drawable.HeightDp,
                HorizontalOptions = LayoutOptions.Fill,
            };
            Press(shelf, row);
            shelves.Add(shelf);

            if (row.SeriesName is not null)
            {
                var label = new Label
                {
                    Text = $"{row.SeriesName} · {BookLines.Spell(row.Books.Count)} "
                        + (row.Books.Count == 1 ? "book" : "books"),
                    Style = PaperResources.Token<Style>("SecondaryLabel"),
                    Margin = new Thickness(DesignTokens.Space.X4, DesignTokens.Space.X1, 0, 0),
                };

                // The rename card had no route to it either: it was built, tested, and opened by
                // nothing. The series' own name is the obvious thing to press to change it.
                var rename = new TapGestureRecognizer();
                var series = row.Books[0].Series;
                var current = row.SeriesName;
                rename.Tapped += (_, _) =>
                {
                    if (series is not null)
                        AskForName(series, current);
                };
                label.GestureRecognizers.Add(rename);

                shelves.Add(label);
            }
        }
    }

    /// <summary>
    /// A shelf is one painted canvas, so a tap arrives as a coordinate and
    /// <see cref="ShelfRow.At"/> turns it back into a book — the painter's own walk, read backwards,
    /// which is why it lives in Domain beside the painting rather than here.
    ///
    /// <para>A tap that lands on a bookend, a gap or bare shelf opens nothing. Nudging the nearest
    /// book open instead would mean the shelf sometimes opens a book nobody pointed at.</para>
    /// </summary>
    private void Press(View shelf, ShelfRow row)
    {
        var open = new TapGestureRecognizer();
        open.Tapped += (sender, args) =>
        {
            if (args.GetPosition(shelf) is { } point && row.At(point.X) is { } book)
            {
                _ = OpenBook(book.Id);
            }
        };
        shelf.GestureRecognizers.Add(open);
    }

    private async Task OpenBook(Guid draftId)
    {
        BookRecord? book;
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            book = await scope.ServiceProvider.GetRequiredService<ProjectService>()
                .GetBookAsync(draftId, CancellationToken.None);
        }

        if (book is not null)
        {
            await Navigation.PushAsync(new BookPage(book));
        }
    }

    /// <summary>
    /// Asks what the series is called, on paper. Renaming is reversible by renaming back, so it is
    /// done and then offered back through the fold rather than asked about first.
    /// </summary>
    private void AskForName(string series, string? current)
    {
        if (!Guid.TryParse(series, out var seriesId))
        {
            return;
        }

        Present(RenameCard.Build(
            "Name this series",
            "The Tidewater Books",
            current,
            named: name => _ = Rename(seriesId, name, current),
            dismissed: Dismiss));
    }

    private async Task Rename(Guid seriesId, string? name, string? previous)
    {
        await Write(seriesId, name);

        // The undo puts the old name back and offers nothing further — an undo that offers its own
        // undo is a loop that says "renamed to X" about a name nobody typed.
        OfferUndo(
            string.IsNullOrWhiteSpace(name) ? "Series name cleared" : $"Series renamed to {name.Trim()}",
            () => _ = Write(seriesId, previous).ContinueWith(
                _ => Dispatcher.Dispatch(() => _ = LoadAsync()), TaskScheduler.Default));

        await LoadAsync();
    }

    private static async Task Write(Guid seriesId, string? name)
    {
        using var scope = IPlatformApplication.Current!.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ProjectService>()
            .RenameSeriesAsync(seriesId, name, CancellationToken.None);
    }
}
