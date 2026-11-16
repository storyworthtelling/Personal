using Folio.Domain.Diagnostics;
using Storyworthtelling.Maui.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Folio.Domain.Analysis;
using Folio.Domain.Readiness;
using Folio.Domain.Reading;
using Folio.Domain.Shelf;
using Folio.Infrastructure.Services;
using Storyworthtelling.Maui;

namespace Folio.Android.Pages;

/// <summary>One draft in the list under the chart.</summary>
public sealed record DraftRow(Guid Id, int Number, string Date, string Words, bool ReportReady);

/// <summary>One reader's score on the book page: who, on what question, the number and why.</summary>
public sealed record ScoreRow(string Name, string Question, string Line, string Reason)
{
    /// <summary>This reader's scores across drafts, for its trend line; fewer than two draws nothing.</summary>
    public IReadOnlyList<DraftScore> History { get; init; } = [];
}

/// <summary>What the book page needs to draw itself.</summary>
public sealed record BookPageModel(
    string Title,
    ImageSource? Jacket,
    string Standing,
    string Drafts,
    string Measure,
    IReadOnlyList<DraftFindings> Readiness,
    IReadOnlyList<DraftRow> Rows)
{
    /// <summary>"Readers · draft 3", or null when no draft has been read with scores yet.</summary>
    public string? ScoresHeading { get; init; }

    /// <summary>The newest scored draft's readers, in reading order, each with how it moved.</summary>
    public IReadOnlyList<ScoreRow> Scores { get; init; } = [];

    /// <summary>
    /// The read, phrased. Every sentence here is assembled by a tested function in Domain
    /// (<see cref="BookLines"/>, <see cref="LengthGap"/>) and this only puts the pieces on the page —
    /// so the shelf and this screen cannot come to different words about the same book.
    /// </summary>
    public static BookPageModel From(BookRecord book) => new(
        book.Title,
        book.CoverPath is { } path && File.Exists(path) ? ImageSource.FromFile(path) : null,
        BookLines.Standing(book.InSeries, book.SeriesSequence, book.SeriesLength, book.Stage),
        BookLines.Drafts(book.Drafts.Count, book.Drafts.Count > 0 ? book.Drafts[^1].Created : null),
        MeasureLine(book),
        book.Readiness,
        // Newest first, which is the opposite of the chart beneath which it sits — and deliberately:
        // the chart is a history and reads left to right, the list is a record and the draft the
        // author is working on belongs at the top of it.
        [.. book.Drafts.OrderByDescending(draft => draft.Number).Select(Row)])
    {
        ScoresHeading = book.ScoredDraftNumber is { } number ? $"Readers · draft {number}" : null,
        Scores = [.. book.ReaderScores.Select(score => new ScoreRow(
            score.Name, $"{score.Role} · {score.Question}", ReaderScores.Line(score.Score, score.Previous), score.Reason)
        {
            History = score.History,
        })],
    };

    private static DraftRow Row(BookDraft draft) => new(
        draft.Id,
        draft.Number,
        draft.Created.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture),
        draft.WordCount > 0 ? draft.WordCount.ToString("N0", CultureInfo.CurrentCulture) : "—",
        draft.ReportReady);

    // "Literary · 94,200 words, inside the genre norm" — the genre only when one was inferred.
    private static string MeasureLine(BookRecord book)
    {
        var length = LengthGap.Brief(book.Length.WordCount, book.Length.TargetLow, book.Length.TargetHigh);
        return string.IsNullOrWhiteSpace(book.Genre)
            ? length
            : string.IsNullOrEmpty(length) ? book.Genre : $"{book.Genre} · {length}";
    }
}

/// <summary>
/// One book: what it is, how ready it is, and its drafts.
///
/// <para><b>The ground is paper here.</b> Only the Work desk is wood — the desk is where books are
/// being handled, and this is a record of one.</para>
///
/// <para><b>Readiness is counts, never a score.</b> Musts falling and Greats rising is what getting
/// better looks like, drawn from findings the report already produced, so the chart never
/// compresses the book into a number. The readers' scores below it are a different thing (the
/// owner's ruling): six numbers, each one reader's answer to its own question, never added up.</para>
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
    private readonly BookRecord record;
    private readonly Button openButton;
    private readonly Dictionary<Guid, Label> marks = [];

    // Which draft's report "Open report" opens. The newest draft is the default; tapping another
    // draft in the list below picks that one instead.
    private Guid selectedDraft;

    public BookPage(BookRecord book) : this(BookPageModel.From(book), book)
    {
    }

    private BookPage(BookPageModel model, BookRecord book)
    {
        record = book;
        selectedDraft = book.LatestDraftId;

        Content = new ScrollView
        {
            Padding = PaperResources.Token<Thickness>("ScreenPadding"),
            Content = body,
        };

        Wrap(FolioArea.Shelf, model.Title);
        Show(model);

        var draft = new Button
        {
            Text = "New draft",
            Style = PaperResources.Token<Style>("SecondaryButton"),
        };
        draft.Clicked += (_, _) => _ = NewDraft();

        openButton = new Button();
        openButton.Clicked += (_, _) => _ = OpenReport();
        SelectDraft(book.LatestDraftId);

        // The primary on the right, the suite's order (owner's ruling, audit W8.3a).
        SetBar(draft, openButton);
    }

    /// <summary>
    /// Opens the rendered report.
    ///
    /// <para>There is no in-app reader behind this any more (D85). The report is a printed document
    /// with a marked-up manuscript in the middle of it, and the screen that paraphrased it was a
    /// second, thinner copy that drifted from the page it was copying. Which makes this button the
    /// whole of the product's reachability, and why <see cref="ReportFile"/> is guarded.</para>
    /// </summary>
    private async Task OpenReport()
    {
        var opening = MauiTapWatch.Action(FolioKeyActions.OpenReport, TimeSpan.FromSeconds(15));
        var opened = await ReportFile.OpenAsync(selectedDraft, record.Title);
        if (opened) opening.Succeeded(); else opening.Failed("the report is not on this phone");
        if (!opened)
        {
            // Said on the page, not in a dialog: the report has been delivered and cleared off the
            // device, or has not been rendered yet. Either way there is nothing here to open.
            Note("The report is not on this phone. It is in the Drive folder beside the manuscript.");
        }
    }

    // Points "Open report" at one draft's report, and marks that draft in the list. The button names
    // the draft only when it is not the newest, so the default reads as plainly "Open report".
    private void SelectDraft(Guid id)
    {
        selectedDraft = id;
        var draft = record.Drafts.First(d => d.Id == id);
        openButton.Text = id == record.LatestDraftId ? "Open report" : $"Open draft {draft.Number} report";
        openButton.IsEnabled = draft.ReportReady;

        foreach (var (draftId, mark) in marks)
            mark.Text = draftId == id ? "●" : string.Empty;
    }

    private async Task NewDraft()
    {
        var page = IPlatformApplication.Current!.Services.GetRequiredService<NewProjectPage>();
        page.PreviousDraftId = record.LatestDraftId;
        page.PreviousTitle = record.Title;
        await Navigation.PushAsync(page);
    }

    // A status sentence under the bar, per §8 — not a toast, and never an alert.
    private void Note(string sentence)
    {
        body.Add(new Label
        {
            Text = sentence,
            Style = PaperResources.Token<Style>("SecondaryLabel"),
            Margin = new Thickness(0, DesignTokens.Space.X4, 0, 0),
        });
    }

    private void Show(BookPageModel book)
    {
        body.Add(Hero(book));

        body.Add(SectionLabel("Readiness"));
        body.Add(Chart(book.Readiness));
        body.Add(Legend(book.Readiness));

        if (book.ScoresHeading is { } heading && book.Scores.Count > 0)
        {
            body.Add(SectionLabel(heading));
            foreach (var score in book.Scores)
            {
                body.Add(Score(score, last: ReferenceEquals(score, book.Scores[^1])));
            }
        }

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
                new Label { Text = book.Standing, FontFamily = "SerifSemibold", FontSize = PaperResources.Token<double>("HeadingSize"), LineHeight = 1.2 },
                new Label
                {
                    Text = book.Drafts,
                    FontSize = PaperResources.Token<double>("SecondarySize"),
                    TextColor = PaperResources.Colour("InkSoft"),
                    Margin = new Thickness(0, 4, 0, 0),
                },
                new Label
                {
                    Text = book.Measure,
                    FontSize = PaperResources.Token<double>("SecondarySize"),
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

    private static View Chart(IReadOnlyList<DraftFindings> readiness)
    {
        var chart = new GraphicsView
        {
            Drawable = new ReadinessChartDrawable
            {
                Plot = ReadinessChart.Plot(readiness),
                Labels = TierNames.ToDictionary(
                    entry => entry.Key,
                    entry => $"{entry.Value} {Latest(readiness, entry.Key)}"),
            },
            HeightRequest = ReadinessChartDrawable.FrameHeight,
            Margin = new Thickness(0, 12, 0, 0),
            InputTransparent = true,
        };

        // The chart in one sentence for a screen reader (§17, audit W9.3): how many drafts, and the latest counts.
        SemanticProperties.SetDescription(chart,
            $"Findings across {readiness.Count} {(readiness.Count == 1 ? "draft" : "drafts")}; latest: "
            + string.Join(", ", TierNames.Select(entry => $"{entry.Value} {Latest(readiness, entry.Key)}")) + ".");
        return chart;
    }

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
        var row = new HorizontalStackLayout { Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };

        foreach (var tier in Enum.GetValues<ReadinessTier>())
        {
            if (!readiness.Any(draft => draft.Counts.ContainsKey(tier)))
            {
                continue;
            }

            row.Children.Add(new HorizontalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Border
                    {
                        WidthRequest = 9,
                        HeightRequest = 9,
                        StrokeThickness = 0,
                        BackgroundColor = ReadinessChartDrawable.InkFor(tier),
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = Storyworthtelling.Maui.DesignTokens.Shape.RadiusCard },
                        VerticalOptions = LayoutOptions.Center,
                    },
                    new Label
                    {
                        Text = TierNames[tier],
                        FontSize = PaperResources.Token<double>("LabelSize"),
                        TextColor = PaperResources.Colour("InkSoft"),
                        VerticalOptions = LayoutOptions.Center,
                    },
                },
            });
        }

        return row;
    }

    // One reader: the name and its question, the score and how it moved, and the sentence saying why.
    private static View Score(ScoreRow row, bool last)
    {
        var line = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            ],
            ColumnSpacing = 12,
        };
        var who = new Label { Text = row.Name, FontFamily = "SerifSemibold", FontSize = PaperResources.Token<double>("HeadingSize") };
        var number = new Label
        {
            Text = row.Line,
            Style = PaperResources.Token<Style>("MonoLabel"),
            FontSize = PaperResources.Token<double>("LabelSize"),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
        };
        line.Children.Add(who);
        Grid.SetColumn(who, 0);

        // The reader's scores across drafts (R13, R37), beside today's, once there are two to join.
        if (row.History.Count >= 2)
        {
            var trend = new GraphicsView
            {
                Drawable = new ScoreTrendDrawable { History = row.History },
                WidthRequest = ScoreTrendDrawable.Width,
                HeightRequest = ScoreTrendDrawable.Height,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
            };
            line.Children.Add(trend);
            Grid.SetColumn(trend, 1);
        }

        line.Children.Add(number);
        Grid.SetColumn(number, 2);

        var stack = new VerticalStackLayout
        {
            Padding = new Thickness(0, 12),
            Children =
            {
                line,
                new Label { Text = row.Question, FontSize = PaperResources.Token<double>("LabelSize"), TextColor = PaperResources.Colour("InkSoft") },
                new Label
                {
                    Text = row.Reason,
                    FontSize = PaperResources.Token<double>("SecondarySize"),
                    TextColor = PaperResources.Colour("InkSoft"),
                    Margin = new Thickness(0, 4, 0, 0),
                },
            },
        };

        return last
            ? stack
            : new VerticalStackLayout
            {
                Children =
                {
                    stack,
                    new BoxView { HeightRequest = DesignTokens.Shape.HairlineDp, Color = PaperResources.Colour("Hairline") },
                },
            };
    }

    private static View SectionLabel(string text) => new Label
    {
        Text = text,
        FontFamily = "SansSemibold",
        FontSize = PaperResources.Token<double>("LabelSize"),
        TextColor = PaperResources.Colour("InkSoft"),
        Margin = new Thickness(0, 24, 0, 8),
    };

    // A draft: its number, when it arrived, and how long it was. The count is monospaced and
    // tabular so the column reads down as numbers rather than as words that happen to be digits.
    private View Row(DraftRow row, bool last)
    {
        var line = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            ],
            ColumnSpacing = 12,
            Padding = new Thickness(0, 12),
        };

        var number = new Label { Text = row.Number.ToString(), FontFamily = "SerifSemibold", FontSize = PaperResources.Token<double>("HeadingSize") };
        var date = new Label
        {
            Text = row.Date,
            FontSize = PaperResources.Token<double>("LabelSize"),
            TextColor = PaperResources.Colour("InkSoft"),
            VerticalOptions = LayoutOptions.End,
        };
        var words = new Label
        {
            Text = row.Words,
            Style = PaperResources.Token<Style>("MonoLabel"),
            FontSize = PaperResources.Token<double>("LabelSize"),
            TextColor = PaperResources.Colour("InkSoft"),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
        };

        var mark = new Label
        {
            FontSize = PaperResources.Token<double>("LabelSize"),
            TextColor = PaperResources.Colour("InkSoft"),
            VerticalOptions = LayoutOptions.Center,
        };
        marks[row.Id] = mark;
        var pick = new TapGestureRecognizer();
        pick.Tapped += (_, _) => SelectDraft(row.Id);
        line.GestureRecognizers.Add(pick);

        var label = new HorizontalStackLayout { Spacing = 8, Children = { number, mark } };
        line.Children.Add(label);
        line.Children.Add(date);
        line.Children.Add(words);
        Grid.SetColumn(label, 0);
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
