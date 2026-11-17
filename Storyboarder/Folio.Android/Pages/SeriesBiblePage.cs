using Microsoft.Extensions.DependencyInjection;
using Folio.Domain.Series;
using Folio.Infrastructure.Services;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;

namespace Folio.Android.Pages;

/// <summary>
/// A series' bible on the phone (release G, R26): every entry, what the books say of it, and the owner's
/// correction, which wins over the books. Tapping an entry opens a paper card to correct it; the line
/// changes in place. The same bible is printed and sent to the tablet beside each series book's report.
/// </summary>
public class SeriesBiblePage : FolioBinderPage
{
    private static readonly (BibleKind Kind, string Heading)[] Kinds =
    [
        (BibleKind.Character, "Characters"),
        (BibleKind.Setting, "Settings"),
        (BibleKind.Rule, "World rules"),
        (BibleKind.Event, "Timeline events"),
        (BibleKind.Thread, "Open threads"),
    ];

    private readonly VerticalStackLayout body = new();
    private readonly Label status = new() { Style = PaperResources.Token<Style>("StatusLine") };
    private readonly Label problem = new() { Style = PaperResources.Token<Style>("ErrorLabel"), IsVisible = false };
    private readonly Guid draftId;

    public SeriesBiblePage(Guid draftId)
    {
        this.draftId = draftId;
        Content = new ScrollView
        {
            Padding = PaperResources.Token<Thickness>("ScreenPadding"),
            Content = body,
        };
        Wrap(FolioArea.Shelf, "Series bible");
        status.Text = "Opening the series bible.";
        body.Add(status);
        body.Add(problem);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (body.Children.Count > 2)
            return;

        SeriesBibleView? view;
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            view = await scope.ServiceProvider.GetRequiredService<ProjectService>()
                .GetSeriesBibleAsync(draftId, CancellationToken.None);
        }

        if (view is null || view.Bible.Entries.Count == 0)
        {
            status.Text = "The series bible is written once a book in the series has been read.";
            return;
        }

        status.Text = $"{view.Name}: what the books have established. Tap an entry to correct it; your correction wins over the books.";
        foreach (var (kind, heading) in Kinds)
        {
            var entries = view.Bible.Entries.Where(entry => entry.Kind == kind).ToList();
            if (entries.Count == 0)
                continue;

            body.Add(new Label
            {
                Text = heading,
                FontFamily = "SansSemibold",
                FontSize = PaperResources.Token<double>("LabelSize"),
                TextColor = PaperResources.Colour("InkSoft"),
                Margin = new Thickness(0, DesignTokens.Space.X6, 0, DesignTokens.Space.X2),
            });
            foreach (var entry in entries)
                body.Add(Row(view.SeriesId, entry));
        }
    }

    // One entry: its name, what it now is, and whose word that is. A tap opens the correction card.
    private View Row(Guid seriesId, BibleEntry entry)
    {
        var current = new Label
        {
            Text = entry.Current,
            FontSize = PaperResources.Token<double>("SecondarySize"),
            TextColor = PaperResources.Colour("Ink"),
        };
        var whose = new Label
        {
            Text = Whose(entry),
            FontSize = PaperResources.Token<double>("LabelSize"),
            TextColor = PaperResources.Colour("InkSoft"),
        };
        var row = new VerticalStackLayout
        {
            Padding = new Thickness(0, DesignTokens.Space.X3),
            Children =
            {
                new Label { Text = entry.Name, FontFamily = "SerifSemibold", FontSize = PaperResources.Token<double>("HeadingSize") },
                current,
                whose,
            },
        };

        var shown = entry;
        async Task CorrectThis() => shown = await Correct(seriesId, shown, current, whose);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => _ = CorrectThis();
        row.GestureRecognizers.Add(tap);
        return row;
    }

    // Answers the entry as it now stands, so a second tap starts from the first correction.
    private async Task<BibleEntry> Correct(Guid seriesId, BibleEntry entry, Label current, Label whose)
    {
        var said = entry.States.Count == 0 ? null : $"{entry.States[^1].Book}: {entry.States[^1].Text}";
        var correction = await PaperCards.AskText(
            this, entry.Name, said, "What is true of it (leave empty to go by the books)",
            entry.Correction.Length > 0 ? entry.Correction : entry.Current, "Keep", multiLine: true);
        if (correction is null)
            return entry;

        bool kept;
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            kept = await scope.ServiceProvider.GetRequiredService<ProjectService>()
                .CorrectSeriesBibleAsync(seriesId, entry.Kind, entry.Name, correction, CancellationToken.None);
        }

        if (!kept)
        {
            problem.Text = "The series bible on this phone couldn’t be read, so nothing was changed.";
            problem.IsVisible = true;
            return entry;
        }

        // The Drive doc follows the correction (owner's request), so its history keeps it. Best-effort and
        // quiet: a write that fails is made at the bible's next change.
        _ = IPlatformApplication.Current!.Services.GetRequiredService<SeriesBibleBackup>()
            .WriteAsync(seriesId, $"your correction to {entry.Name}", null, CancellationToken.None);

        var corrected = entry with { Correction = correction.Trim() };
        current.Text = corrected.Current;
        whose.Text = Whose(corrected);
        problem.IsVisible = false;
        return corrected;
    }

    private static string Whose(BibleEntry entry) =>
        entry.Correction.Length > 0
            ? "Your correction"
            : entry.States.Count == 0 ? string.Empty : "From " + string.Join(", ", entry.States.Select(state => state.Book).Distinct());
}
