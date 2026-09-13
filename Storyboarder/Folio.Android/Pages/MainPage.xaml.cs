using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Folio.Domain.Time;
using Folio.Domain.External;
using Folio.Domain.Model;
using Folio.Infrastructure.Services;
using Storyworthtelling.Maui;
using Storyworthtelling.Voice;

namespace Folio.Android.Pages;

/// <summary>
/// The Work desk: the app's home, and where books are being handled.
///
/// <para>It was a list with a Display label at the top and two quiet buttons in the bottom bar, one
/// of which was how you reached Settings — which is the shape P9 found in Foreword and the reason
/// the binder exists. Settings is a tab now, and the bar carries the one thing this screen is for.
/// </para>
/// </summary>
public partial class MainPage : FolioBinderPage
{
    private readonly ObservableCollection<ProjectRow> rows = [];
    private readonly Dictionary<Guid, NovelStatus> statuses = [];
    private IDispatcherTimer? refreshTimer;
    private string voice = string.Empty;

    public MainPage()
    {
        InitializeComponent();

        // The wood runs behind the header as well as the content: the frame draws the desk as the
        // whole well, with the screen header sitting on it.
        SetBackdrop(new GraphicsView { Drawable = new DeskDrawable() });
        Wrap(FolioArea.Work, "Work desk");

        var add = new Button { Text = "Add manuscript" };
        add.Clicked += OnNewProjectClicked;
        SetBar(add);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ConfigWarningLabel.Text = "Folio needs its vault passphrase before it can review anything.";
        ConfigWarningCard.IsVisible = !SettingsStore.IsConfigured;
        LastRunLabel.Text = SettingsStore.LastRunSummary;

        _ = RefreshAsync();
        _ = ShowWelcomeAsync();

        refreshTimer = Dispatcher.CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(5);
        refreshTimer.Tick += async (_, _) => await RefreshAsync();
        refreshTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        refreshTimer?.Stop();
        refreshTimer = null;
    }

    private async Task RefreshAsync()
    {
        try
        {
            IReadOnlyList<NovelStatus> list;
            using (var scope = IPlatformApplication.Current!.Services.CreateScope())
            {
                var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
                list = await projects.ListAsync(CancellationToken.None);
            }

            statuses.Clear();
            foreach (var status in list)
                statuses[status.Id] = status;

            rows.Clear();
            foreach (var row in list.Select(ProjectRow.From))
                rows.Add(row);

            Redraw();
            LastRunLabel.Text = SettingsStore.LastRunSummary;
        }
        catch
        {
            // A transient read failure shouldn't crash the page; the next tick retries.
        }
    }

    // A greeting written in the owner's voice, composed fresh each time the page appears. Shown only when a
    // voice profile has been learned; otherwise VoicedMessenger returns the empty fallback and the line
    // stays hidden rather than showing generic copy. Never blocks or crashes the page.
    private async Task ShowWelcomeAsync()
    {
        if (!SettingsStore.IsConfigured)
        {
            voice = string.Empty;
            Redraw();
            return;
        }

        string message;
        try
        {
            var messenger = IPlatformApplication.Current!.Services.GetRequiredService<VoicedMessenger>();
            var context = rows.Count == 0 ? "no manuscripts yet" : $"{rows.Count} manuscript(s) under review";
            message = await messenger.ComposeAsync(
                new MessageRequest("welcome", context, HighVisibility: true), fallback: string.Empty, CancellationToken.None);
        }
        catch
        {
            message = string.Empty;
        }

        voice = message;
        Redraw();
    }

    /// <summary>
    /// A long press offers what can be done with this book, on §7's action card — paper over a haze,
    /// tap-out to dismiss. It was a system action sheet, which is a dialog: it interrupts, it is not
    /// the app's own material, and its destructive row asks a question. Deleting here is undo-first
    /// via the dog-ear instead, so nothing on the card asks "are you sure".
    /// </summary>
    private void ShowActions(NovelStatus status)
    {
        var buttons = new List<string>();

        // A finished book can be put away. It leads the card and carries the weight — archiving is
        // prominent and deleting is merely available, which is the whole of D19.
        if (status.Stage == "Completed" && !status.Shelved)
            buttons.Add(ShelveAction);

        // The jacket: pick one, or have another painted. Both are undo-first, so neither asks.
        buttons.Add(ReplaceCoverAction);
        buttons.Add(RepaintCoverAction);

        // The finished revision report: open/share the PDF, or read it in the app.
        if (status.AnalysisReady)
        {
            buttons.Add(OpenReportPdfAction);
            buttons.Add(ReadReportAction);
        }

        // Drafts: start the next draft of this book from a re-picked doc, and — once there is a
        // predecessor — see what the last revision changed.
        buttons.Add(NewDraftAction);
        if (status.HasPreviousDraft)
            buttons.Add(DiffAction);

        // Series: start the NEXT BOOK (a new manuscript that continues this one), so the pipeline reuses this
        // book's canon for the cross-book continuity read and returning-character look.
        buttons.Add(NextBookAction);

        var isActive = status.Stage is not ("Completed" or "Failed" or "Cancelled");
        if (isActive)
        {
            buttons.Add(RetryAction);
            buttons.Add(StopAction);
        }

        var actions = buttons
            .Select(label => new CardAction(label, GlyphFor(label), WeightFor(label), () => Chose(label, status)))
            .ToList();

        // Available, not offered: last, muted, no colour, no confirmation.
        actions.Add(new CardAction(DeleteAction, Glyphs.Bin, ActionWeight.Quiet, () => Chose(DeleteAction, status)));

        Present(ActionCard.Build(status.Title, ProjectRow.From(status).Summary, actions, Dismiss));
    }

    private async void Chose(string label, NovelStatus status)
    {
        switch (label)
        {
            case ShelveAction: await Shelve(status); break;
            case ReplaceCoverAction: await ReplaceCover(status); break;
            case RepaintCoverAction: await RepaintCover(status); break;
            case DeleteAction: await DeleteProject(status); break;
            case OpenReportPdfAction: await ShareReportPdf(status); break;
            case ReadReportAction: await OpenReport(status); break;
            case NewDraftAction: await StartNewDraft(status); break;
            case NextBookAction: await StartNextBook(status); break;
            case DiffAction: await ShowDraftDiff(status); break;
            case RetryAction: await RetryProject(status); break;
            case StopAction: await StopProject(status); break;
        }
    }

    /// <summary>
    /// Putting the book away is what a finished one is mostly for, so it takes the weighted row;
    /// failing that, reaching the report does.
    /// </summary>
    private static ActionWeight WeightFor(string label) =>
        label is ShelveAction ? ActionWeight.Primary : ActionWeight.Normal;

    /// <summary>
    /// Shelves it and offers it straight back. Nothing is asked first: the fold in the corner is the
    /// undo, and letting it close is the confirmation.
    /// </summary>
    private async Task Shelve(NovelStatus status)
    {
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectService>()
                .ShelveAsync(status.Id, CancellationToken.None);
        }

        OfferUndo($"{status.Title} is on the shelf", () => _ = Unshelve(status.Id));
        await RefreshAsync();
    }

    /// <summary>
    /// Puts a jacket of the owner's choosing on the book. Picking a particular file is already the
    /// deliberate act, so nothing is asked — and the fold puts the old one back.
    /// </summary>
    private async Task ReplaceCover(NovelStatus status)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        var uri = await SafFilePicker.OpenDocumentAsync(activity, "image/*");
        if (uri is null)
        {
            return;
        }

        byte[] bytes;
        try
        {
            using var stream = activity.ContentResolver!.OpenInputStream(uri)
                ?? throw new IOException("Could not open the image.");
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            bytes = buffer.ToArray();
        }
        catch
        {
            LastRunLabel.Text = "That image could not be read.";
            return;
        }

        var (was, wasOwners) = (status.CoverPath, status.CoverIsOwners);
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectService>()
                .ReplaceCoverAsync(status.Id, bytes, CancellationToken.None);
        }

        OfferUndo("Cover replaced", () => _ = RestoreCover(status.Id, was, wasOwners));
        await RefreshAsync();
    }

    /// <summary>
    /// Has another jacket painted.
    ///
    /// <para>The plan had this <i>ask first</i> when the owner had replaced the cover themselves.
    /// It does not ask: every jacket is written to its own file and nothing is overwritten, so the
    /// old one is still on disk and the fold genuinely puts it back. The intent — do not quietly
    /// destroy a cover somebody chose — is kept by making it reversible rather than by a prompt.</para>
    /// </summary>
    private async Task RepaintCover(NovelStatus status)
    {
        var (was, wasOwners) = (status.CoverPath, status.CoverIsOwners);
        LastRunLabel.Text = $"Painting a jacket for {status.Title}…";

        try
        {
            using var scope = IPlatformApplication.Current!.Services.CreateScope();
            var gemini = scope.ServiceProvider.GetRequiredService<IGeminiImageClient>();
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();

            var report = await projects.GetReportAsync(status.Id, CancellationToken.None);
            var jacket = await gemini.GenerateJacket(
                status.Title,
                report?.Genre ?? string.Empty,
                report?.Logline ?? string.Empty,
                ArtStyles.Default,
                CancellationToken.None);

            await projects.SetPaintedCoverAsync(status.Id, jacket, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A jacket is never the point of the app: say so quietly and leave the book as it was.
            LastRunLabel.Text = "Couldn't paint a jacket: " + ex.Message;
            return;
        }

        LastRunLabel.Text = SettingsStore.LastRunSummary;
        OfferUndo(
            wasOwners ? "Your cover was replaced by a painted one" : "Cover repainted",
            () => _ = RestoreCover(status.Id, was, wasOwners));
        await RefreshAsync();
    }

    private async Task RestoreCover(Guid id, string? path, bool wasOwners)
    {
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectService>()
                .RestoreCoverAsync(id, path, wasOwners, CancellationToken.None);
        }

        await RefreshAsync();
    }

    private async Task Unshelve(Guid id)
    {
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectService>()
                .UnshelveAsync(id, CancellationToken.None);
        }

        await RefreshAsync();
    }

    private static string GlyphFor(string label) => label switch
    {
        ShelveAction => Glyphs.Shelve,
        ReplaceCoverAction => Glyphs.Picture,
        RepaintCoverAction => Glyphs.Brush,
        OpenReportPdfAction or ReadReportAction => Glyphs.Page,
        NewDraftAction or NextBookAction => Glyphs.NewPage,
        DiffAction => Glyphs.Compare,
        RetryAction => Glyphs.Again,
        StopAction => Glyphs.Stop,
        _ => Glyphs.Page,
    };

    /// <summary>
    /// Lays the desk out from what is on it: the book being read now with its details on a note
    /// card, then the ones waiting with their state on a post-it.
    ///
    /// <para>Rebuilt whole on each refresh rather than diffed. The list it replaced diffed because a
    /// CollectionView scrolls and loses its place when its items are swapped; a desk is a handful of
    /// books that fits on one screen, and at that size a diff is machinery with nothing to buy.</para>
    /// </summary>
    private void Redraw()
    {
        Desk.Clear();

        var onDesk = rows
            .Select(row => statuses.TryGetValue(row.Id, out var status) ? (Row: row, Status: status) : default)
            .Where(pair => pair.Status is not null)
            .ToList();

        if (onDesk.Count == 0)
        {
            Desk.Add(new Label
            {
                Text = "Nothing on the desk. Add a manuscript and Folio starts reading.",
                Style = PaperResources.Token<Style>("EmptyStateLabel"),
            });
            return;
        }

        // What is being read is whatever is still running; failing that, the newest thing here.
        var reading = onDesk.FirstOrDefault(pair => Active(pair.Status)) is { Status: not null } running
            ? running
            : onDesk[0];

        Desk.Add(DeskPaper.Strip("Reading now"));
        Desk.Add(Hero(reading.Row, reading.Status));

        if (!string.IsNullOrWhiteSpace(voice))
        {
            Desk.Add(new Label
            {
                Text = voice,
                Style = PaperResources.Token<Style>("VoiceLabel"),
                Margin = new Thickness(2, 16, 2, 0),
            });
        }

        var waiting = onDesk.Where(pair => pair.Row.Id != reading.Row.Id).ToList();
        if (waiting.Count > 0)
        {
            Desk.Add(new BoxView { HeightRequest = 16, Color = Colors.Transparent });
            Desk.Add(DeskPaper.Strip("Also on the desk"));
            Desk.Add(Waiting(waiting));
        }

        Desk.Add(new GraphicsView
        {
            Drawable = new DeskFillerDrawable(),
            HeightRequest = 150,
            Margin = new Thickness(0, 10, 0, 0),
            InputTransparent = true,
        });
    }

    private static bool Active(NovelStatus status) =>
        status.Stage is not ("Completed" or "Failed" or "Cancelled");

    private View Hero(ProjectRow row, NovelStatus status)
    {
        var lines = new List<View>
        {
            new Label { Text = row.Title, FontFamily = "SerifSemibold", FontSize = 17, LineHeight = 1.15 },
            new Label
            {
                Text = row.Summary,
                FontSize = 14,
                TextColor = PaperResources.Colour(status.NeedsAttention ? "ProblemMark" : "InkBlue"),
                Margin = new Thickness(0, 8, 0, 0),
            },
        };

        var card = DeskPaper.Card([.. lines]);
        card.Rotation = 0.8;
        card.VerticalOptions = LayoutOptions.Start;

        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 14,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var cover = new FlatBook(row.Title, Jacket(status), 118, 148, tilt: -1.6)
        {
            VerticalOptions = LayoutOptions.Start,
        };
        Press(cover, status);

        grid.Children.Add(cover);
        grid.Children.Add(card);
        Grid.SetColumn(cover, 0);
        Grid.SetColumn(card, 1);
        return grid;
    }

    private View Waiting(IReadOnlyList<(ProjectRow Row, NovelStatus Status)> books)
    {
        var tilts = new[] { 1.4, -2.2, 0.9 };
        var noteTilts = new[] { -1.8, 1.2, -0.9 };
        var strip = new HorizontalStackLayout { Spacing = 11, Margin = new Thickness(0, 10, 0, 0) };

        for (var i = 0; i < books.Count; i++)
        {
            var (row, status) = books[i];
            var note = DeskPaper.PostIt(
                row.Title,
                row.Summary,
                PaperResources.Colour(status.NeedsAttention ? "ProblemMark" : "InkSoft"));
            note.Rotation = noteTilts[i % noteTilts.Length];

            var cover = new FlatBook(row.Title, Jacket(status), 88, 110, tilts[i % tilts.Length]);
            Press(cover, status);

            strip.Children.Add(new VerticalStackLayout
            {
                WidthRequest = 88,
                Spacing = 7,
                Children = { cover, note },
            });
        }

        return strip;
    }

    /// <summary>
    /// The painted jacket, or null for plain cloth. A book with no cover is the ordinary case, and a
    /// recorded cover whose file has gone is the same thing rather than a broken image.
    /// </summary>
    private static ImageSource? Jacket(NovelStatus status) =>
        status.CoverPath is { } path && File.Exists(path) ? ImageSource.FromFile(path) : null;

    // A book is opened by pressing it; what can be done with it is a long press, per the frame.
    private void Press(View book, NovelStatus status)
    {
        var open = new TapGestureRecognizer();
        open.Tapped += (_, _) => ShowActions(status);
        book.GestureRecognizers.Add(open);
    }

    private const string ShelveAction = "Put on the shelf";
    private const string ReplaceCoverAction = "Replace cover…";
    private const string RepaintCoverAction = "Repaint the cover";
    private const string RetryAction = "Retry now";
    private const string StopAction = "Stop review";
    private const string DeleteAction = "Delete manuscript";
    private const string NewDraftAction = "New draft (re-pick doc)";
    private const string NextBookAction = "Start next book (new manuscript)";
    private const string DiffAction = "What changed since last draft";
    private const string OpenReportPdfAction = "Open the report PDF";
    private const string ReadReportAction = "Read report in app";

    // Opens/shares the rendered revision-report PDF.
    private async Task ShareReportPdf(NovelStatus status)
    {
        string? path;
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            path = await projects.GetAnalysisPdfPathAsync(status.Id, CancellationToken.None);
        }
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            await DisplayAlert("Report", "The report PDF could not be found.", "OK");
            return;
        }
        await Share.RequestAsync(new ShareFileRequest
        {
            Title = $"{status.Title} — Analysis",
            File = new ShareFile(path)
        });
    }

    // Opens the browsable revision report in the app.
    private async Task OpenReport(NovelStatus status)
    {
        Folio.Domain.Analysis.BookAnalysisResult? report;
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            report = await projects.GetReportAsync(status.Id, CancellationToken.None);
        }
        if (report is null)
        {
            await DisplayAlert("Report", "The revision report isn't ready yet.", "OK");
            return;
        }
        await Navigation.PushAsync(new ReportPage(status.Title, report));
    }

    // Starts the next draft of this book: opens the Drive picker in "new draft" mode, so the doc the user
    // picks becomes draft N+1 of the same project (ProjectService.CreateNextDraftAsync).
    private async Task StartNewDraft(NovelStatus status)
    {
        var page = IPlatformApplication.Current!.Services.GetRequiredService<NewProjectPage>();
        page.PreviousDraftId = status.Id;
        page.PreviousTitle = status.Title;
        await Navigation.PushAsync(page);
    }

    // Starts the next book of a series: opens the Drive picker in "next book" mode, so the doc the user picks
    // becomes a NEW book (its own project) linked to this one, and the pipeline reuses this book's canon
    // (ProjectService.CreateNextBookAsync).
    private async Task StartNextBook(NovelStatus status)
    {
        var page = IPlatformApplication.Current!.Services.GetRequiredService<NewProjectPage>();
        page.PreviousBookId = status.Id;
        page.PreviousBookTitle = status.Title;
        await Navigation.PushAsync(page);
    }

    // Shows what the last revision moved: the report findings it resolved and newly raised.
    private async Task ShowDraftDiff(NovelStatus status)
    {
        Folio.Domain.Analysis.DraftDiff diff;
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            diff = await projects.GetDraftDiffAsync(status.Id, CancellationToken.None);
        }

        if (diff.PreviousDraftId is null)
        {
            await DisplayAlert("What changed", "This is the first draft — there's nothing to compare against yet.", "OK");
            return;
        }

        await Navigation.PushAsync(new DraftDiffPage(status.Title, status.DraftNumber, diff));
    }

    // Clears the stall and kicks the pipeline immediately, rather than waiting for the next periodic
    // window.
    private async Task RetryProject(NovelStatus status)
    {
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            await projects.RetryAsync(status.Id, CancellationToken.None);
        }
        PipelineWorker.RunNow(global::Android.App.Application.Context);
        await RefreshAsync();
    }

    // Stops a running review but keeps the card and any finished report.
    private async Task StopProject(NovelStatus status)
    {
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            await projects.CancelAsync(status.Id, CancellationToken.None);
        }
        await RefreshAsync();
    }

    // Removes a manuscript entirely (rows + rendered files) after a confirm.
    private async Task DeleteProject(NovelStatus status)
    {
        var confirm = await DisplayAlert(
            "Delete this manuscript?",
            $"“{status.Title}” and its report will be removed for good. This can’t be undone.",
            "Delete", "Keep");
        if (!confirm)
            return;

        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            await projects.DeleteAsync(status.Id, CancellationToken.None);
        }
        await RefreshAsync();
    }

    private async void OnNewProjectClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(
            IPlatformApplication.Current!.Services.GetRequiredService<NewProjectPage>());

    private async void OnSettingsClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(
            IPlatformApplication.Current!.Services.GetRequiredService<SetupPage>());

    // One phase chip on a project card. Public so the XAML binding can read it.
    public sealed record PhaseChipRow(string Text, bool IsActive);

    // Display projection of a NovelStatus for the list. Public so the XAML binding can read it.
    public sealed record ProjectRow(
        Guid Id, string Title, string Summary, string? Error, bool HasError,
        IReadOnlyList<PhaseChipRow> Phases)
    {
        public bool SameAs(ProjectRow other) =>
            Id == other.Id && Title == other.Title && Summary == other.Summary &&
            Error == other.Error && Phases.SequenceEqual(other.Phases);

        public static ProjectRow From(NovelStatus s)
        {
            // Loading is words (DESIGN.md §5): one readable sentence for the active stage — and, for the
            // stages that walk a list, the running count so it's clear how much is left.
            var summary = s.Stage switch
            {
                "Created" or "Ingesting" => "Reading the manuscript…",
                "BuildingDesignBible" => DesignBibleSentence(s),
                "RenderingSceneImages" => Sentence("Illustrating the scenes", "scenes", s.SceneImages),
                "ComposingAnalysis" => AnalysisSentence(s),
                "Archiving" => "Delivering the report…",
                "Completed" => "Review delivered.",
                "Failed" => "Stopped.",
                "Cancelled" => "Stopped by you.",
                _ => s.Stage,
            };

            // A book in a series wears a "Book N" badge at the head of its status line.
            if (s.InSeries)
                summary = $"Book {s.SeriesSequence} · {summary}";

            // Before the scene images render, say what the run is projected to cost.
            if (s.Estimate is { } estimate && estimate.Scenes > 0)
                summary = $"{summary} · ~{estimate.Scenes:N0} scene images, about {Approximately(estimate.Duration)} " +
                          $"at your {estimate.LimitingFactor} cap";

            // When a provider rate-limited us, say when queries resume — in local 12-hour time (DESIGN.md §11).
            if (s.RateLimitedUntilUtc is { } until)
            {
                var local = until.ToLocalTime().DateTime;
                var when = local.Date == DateTime.Now.Date ? TimeFormats.Clock12(local) : TimeFormats.Stamp(local);
                summary = $"{summary} · rate-limited, resumes ~{when}";
            }

            var chips = new List<PhaseChipRow>();
            AddChip(chips, "chunks", s.Chunks, s.Stage == "BuildingDesignBible" && !Complete(s.Chunks));
            AddChip(chips, "sheets", s.ReferenceSheets, s.Stage == "BuildingDesignBible" && Complete(s.Chunks));
            AddChip(chips, "scenes", s.SceneImages, s.Stage == "RenderingSceneImages");
            AddChip(chips, "analysis", s.AnalysisNotes, s.Stage == "ComposingAnalysis");

            var showError = s.NeedsAttention;
            return new ProjectRow(
                s.Id, s.Title, summary, showError ? s.LastError : null, showError, chips);
        }

        // The design bible has two sub-steps: read every chunk for characters & settings, then draw a
        // reference sheet for each. Report whichever is in flight so the sentence never stalls.
        private static string DesignBibleSentence(NovelStatus s) =>
            Complete(s.Chunks)
                ? Sentence("Drawing reference sheets", "sheets", s.ReferenceSheets)
                : Sentence("Building the design bible", "chunks", s.Chunks);

        // The report maps every chapter into notes, then consolidates and renders it as a PDF.
        private static string AnalysisSentence(NovelStatus s) =>
            Complete(s.AnalysisNotes)
                ? "Writing the revision report…"
                : Sentence("Analysing the manuscript", "chapters", s.AnalysisNotes);

        private static string Approximately(TimeSpan duration) => duration.TotalDays switch
        {
            >= 14 => $"{Math.Round(duration.TotalDays / 7)} weeks",
            >= 2 => $"{Math.Round(duration.TotalDays)} days",
            >= 1 => "a day",
            >= 0.08 => $"{Math.Max(1, Math.Round(duration.TotalHours))} hours",
            _ => "a few minutes",
        };

        private static bool Complete(PhaseProgress phase) => phase.Total > 0 && phase.Done >= phase.Total;

        private static string Sentence(string verb, string noun, PhaseProgress phase) =>
            phase.Total > 0 ? $"{verb} — {noun} {phase.Done} of {phase.Total}…" : $"{verb}…";

        private static void AddChip(List<PhaseChipRow> chips, string name, PhaseProgress phase, bool active)
        {
            if (phase.Total > 0)
                chips.Add(new PhaseChipRow($"{name} {phase.Done} of {phase.Total}", active));
        }
    }
}
