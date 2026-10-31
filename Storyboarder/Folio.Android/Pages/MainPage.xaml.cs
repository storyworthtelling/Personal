using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Folio.Domain.Time;
using Folio.Domain.External;
using Folio.Domain.Model;
using Folio.Infrastructure.Services;
using Storyworthtelling.Maui;
using Folio.Domain.Voice;
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

    // The desk line per book, in the owner's voice, when one has been composed (D96). Absent for a
    // book whose voice is not available, where the row's own neutral summary stands.
    private readonly Dictionary<Guid, string> voicedLines = [];

    // What an empty desk says, in the owner's voice, once one has been composed. Empty until then, so
    // the neutral invitation stands — an empty shelf is the first thing a new install shows and it
    // must never wait on a model call to say anything at all.
    private string emptyDeskLine = string.Empty;
    private readonly Dictionary<Guid, NovelStatus> statuses = [];
    private IDispatcherTimer? refreshTimer;

    public MainPage()
    {
        InitializeComponent();

        // The frame first — it builds the column the backdrop hangs on. Then the wood, which runs
        // behind the header as well as the content: the desk is the whole well, with the screen
        // header sitting on it.
        Wrap(FolioArea.Work, "Work desk");
        SetBackdrop(new GraphicsView { Drawable = new DeskDrawable() });

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
            await ShowVoicedLinesAsync(list);
            LastRunLabel.Text = SettingsStore.LastRunSummary;
        }
        catch
        {
            // A transient read failure shouldn't crash the page; the next tick retries.
        }
    }

    // The desk's per-project line in the owner's voice (D96). Fetched after the first draw, so the
    // shelf appears immediately with its neutral copy and the voiced line replaces it when it arrives —
    // the line is a nicety and must never be something the screen waits on.
    //
    // ProjectService composes only when a book has actually moved and remembers the result, so this
    // costs nothing on the paints that make up most of a session (coming back from another screen,
    // the refresh tick) rather than a model call per book per glance.
    private async Task ShowVoicedLinesAsync(IReadOnlyList<NovelStatus> list)
    {
        try
        {
            using var scope = IPlatformApplication.Current!.Services.CreateScope();
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();

            var changed = false;

            // An empty desk is the one place in the app with room to speak to the owner directly, and
            // it is not a failure — it is the ordinary first state of a new install.
            if (list.Count == 0)
            {
                var messenger = scope.ServiceProvider.GetRequiredService<VoicedMessenger>();
                var line = await messenger.ComposeAsync(
                    new MessageRequest(VoicedOccasions.EmptyDesk, "no manuscripts yet", HighVisibility: true),
                    fallback: string.Empty, CancellationToken.None);

                if (!string.IsNullOrWhiteSpace(line) && line != emptyDeskLine)
                {
                    emptyDeskLine = line;
                    Redraw();
                }

                return;
            }

            foreach (var status in list)
            {
                var row = rows.FirstOrDefault(r => r.Id == status.Id);
                if (row is null)
                    continue;

                var line = await projects.VoicedLineAsync(status.Id, row.Summary, CancellationToken.None);
                if (line == row.Summary)
                {
                    // No line to show (stopped, or no voice yet): forget any earlier one rather than
                    // leave it on the desk over a state it no longer describes.
                    changed |= voicedLines.Remove(status.Id);
                    continue;
                }

                voicedLines[status.Id] = line;
                changed = true;
            }

            if (changed)
                Redraw();
        }
        catch
        {
            // Voiced copy is layered over neutral copy; a failure here leaves the desk exactly as drawn.
        }
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
        // prominent and deleting is merely available, which is the whole of D19. Adding the route
        // into the book page above it would have quietly taken that lead away, which is what the
        // drawing shows it keeping.
        if (status.Stage == "Completed" && !status.Shelved)
            buttons.Add(ShelveAction);

        buttons.Add(OpenBookAction);

        // The jacket: pick one, or have another painted. Both are undo-first, so neither asks.
        buttons.Add(ReplaceCoverAction);
        buttons.Add(RepaintCoverAction);

        // The finished revision report. One action, not two: the in-app reader is gone (D85) and
        // what is left is the document itself.
        if (status.AnalysisReady)
            buttons.Add(OpenReportPdfAction);

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

        var shown = ProjectRow.From(status);
        Present(ActionCard.Build(
            status.Title,
            string.IsNullOrWhiteSpace(shown.Problem) ? shown.Summary : $"{shown.Summary}\n{shown.Problem}",
            actions, Dismiss));
    }

    private async void Chose(string label, NovelStatus status)
    {
        switch (label)
        {
            case ShelveAction: await Shelve(status); break;
            case ReplaceCoverAction: await ReplaceCover(status); break;
            case RepaintCoverAction: await RepaintCover(status); break;
            case DeleteAction: await DeleteProject(status); break;
            case OpenBookAction: await OpenBook(status.Id); break;
            case OpenReportPdfAction: await OpenReport(status); break;
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
        OpenBookAction => Glyphs.Book,
        OpenReportPdfAction => Glyphs.Page,
        NewDraftAction or NextBookAction => Glyphs.NewPage,
        DiffAction => Glyphs.Compare,
        RetryAction => Glyphs.Again,
        StopAction => Glyphs.Stop,
        _ => Glyphs.Page,
    };

    /// <summary>
    /// Lays the desk out from what is on it: a scrolling column of books, each with its details on an
    /// index card beside it.
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
            // On a card, never on the bare wood: muted ink on the plank is unreadable (DeskDrawable).
            Desk.Add(DeskPaper.Card(new Label
            {
                Text = string.IsNullOrWhiteSpace(emptyDeskLine)
                    ? "Nothing on the desk. Add a manuscript and Folio starts reading."
                    : emptyDeskLine,
                Style = PaperResources.Token<Style>("EmptyStateLabel"),
            }));
            return;
        }

        // One row per book, each a cover with its index card beside it — no headings, no second kind
        // of entry. Order is the only thing that says what matters: whatever is still running first,
        // then a book that has stopped and needs the owner (it must not hide among the others), then
        // the rest as they came.
        var ordered = onDesk
            .OrderBy(pair => Active(pair.Status) && !pair.Status.NeedsAttention ? 0 : Active(pair.Status) ? 1 : 2)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            Desk.Add(BookRow(ordered[i].Row, ordered[i].Status, i));
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

    // Tilts cycle so a stack of books looks laid down by hand rather than ruled.
    private static readonly double[] CoverTilts = [-1.6, 1.4, -0.9, 1.1];
    private static readonly double[] CardTilts = [0.8, -0.7, 0.6, -0.9];

    private View BookRow(ProjectRow row, NovelStatus status, int index)
    {
        // The facts first and always: what the book is doing, and — in red — what is wrong with it. The
        // owner's voice is a note under them, never a replacement for them (it used to replace them, and
        // said "Draft 1 is complete" over a book that had stopped part-way through its reads).
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

        if (!string.IsNullOrWhiteSpace(row.Problem))
        {
            lines.Add(new Label
            {
                Text = row.Problem,
                FontSize = 13,
                TextColor = PaperResources.Colour("ProblemMark"),
                Margin = new Thickness(0, 6, 0, 0),
            });
        }

        if (!status.NeedsAttention && voicedLines.TryGetValue(row.Id, out var voiced) && !string.IsNullOrWhiteSpace(voiced))
        {
            lines.Add(new Label
            {
                Text = voiced,
                Style = PaperResources.Token<Style>("VoiceLabel"),
                Margin = new Thickness(0, 10, 0, 0),
            });
        }

        var card = DeskPaper.Card([.. lines]);
        card.Rotation = CardTilts[index % CardTilts.Length];
        card.VerticalOptions = LayoutOptions.Start;

        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 14,
            Margin = new Thickness(0, index == 0 ? 4 : 18, 0, 0),
        };

        var cover = new FlatBook(row.Title, Jacket(status), 118, 148, tilt: CoverTilts[index % CoverTilts.Length])
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

    /// <summary>
    /// The painted jacket, or null for plain cloth. A book with no cover is the ordinary case, and a
    /// recorded cover whose file has gone is the same thing rather than a broken image.
    /// </summary>
    private static ImageSource? Jacket(NovelStatus status) =>
        status.CoverPath is { } path && File.Exists(path) ? ImageSource.FromFile(path) : null;

    // A book on the desk is work in progress, so pressing it offers what can be done with that
    // work. Opening the BOOK — the record of every draft — is the first thing on that card, and
    // the shelf opens it directly, where a book is a book rather than a job.
    private void Press(View book, NovelStatus status)
    {
        var open = new TapGestureRecognizer();
        open.Tapped += (_, _) => ShowActions(status);
        book.GestureRecognizers.Add(open);
    }

    /// <summary>
    /// Opens the book this draft belongs to: the record of the whole book, not of one draft.
    ///
    /// <para><see cref="BookPage"/> was built, wears the binder, passes every frame guard — and was
    /// constructed by nothing at all. A screen no tap ends at is not a screen, and no test about a
    /// component ever notices, which is what <c>ReportIsReachableTests</c> is for.</para>
    /// </summary>
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

    private const string ShelveAction = "Put on the shelf";
    private const string ReplaceCoverAction = "Replace cover…";
    private const string RepaintCoverAction = "Repaint the cover";
    private const string RetryAction = "Retry now";
    private const string StopAction = "Stop review";
    private const string DeleteAction = "Delete";
    // Named for the draft it starts, as the drawing has it: "Start draft 3…", not a description of
    // the mechanism. The ellipsis is the §7 grammar for an action that opens a picker.
    private const string NewDraftAction = "Start the next draft…";
    private const string NextBookAction = "Start next book (new manuscript)";
    private const string DiffAction = "What changed since last draft";
    private const string OpenBookAction = "Open the book";
    private const string OpenReportPdfAction = "Open the report";

    /// <summary>
    /// Opens the rendered revision report through <see cref="ReportFile"/>.
    ///
    /// <para>This was two actions and two methods — share the PDF, or read a paraphrase of it in the
    /// app — and the second is gone with <c>ReportPage</c>. What is left shares its one risky line
    /// with the book page's own button rather than keeping a second copy of it.</para>
    /// </summary>
    private async Task OpenReport(NovelStatus status)
    {
        if (!await ReportFile.OpenAsync(status.Id, status.Title))
        {
            // The status line, not a dialog (§8): the report has been delivered and cleared off the
            // device, or has not been rendered yet. Either way there is nothing here to open.
            LastRunLabel.Text = "That report is not on this phone — it is in Drive, beside the manuscript.";
        }
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

    // Removes a manuscript — undo-first (§6), like everything else this card offers. Soft-deleted at
    // once (hidden from the desk immediately) and only actually gone once the fold's window closes
    // without being turned; ActionCard's own doc comment is the rule this used to break with a confirm.
    private async Task DeleteProject(NovelStatus status)
    {
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectService>()
                .SoftDeleteAsync(status.Id, CancellationToken.None);
        }
        await RefreshAsync();

        var reversed = false;
        OfferUndo($"“{status.Title}” deleted", () =>
        {
            reversed = true;
            _ = RestoreProject(status.Id);
        });
        CommitUnlessUndone(() => reversed, () => HardDeleteProject(status.Id));
    }

    private async Task RestoreProject(Guid id)
    {
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectService>()
                .RestoreAsync(id, CancellationToken.None);
        }
        await RefreshAsync();
    }

    private async Task HardDeleteProject(Guid id)
    {
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectService>()
                .DeleteAsync(id, CancellationToken.None);
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
    // The words themselves come from DeskStatus, so the heading, the status sentence and the note the
    // owner's voice writes are decided in one place and cannot contradict each other.
    public sealed record ProjectRow(
        Guid Id, string Title, string Summary, string? Error, bool HasError,
        IReadOnlyList<PhaseChipRow> Phases, string Heading = "Reading now", string? Problem = null)
    {
        public bool SameAs(ProjectRow other) =>
            Id == other.Id && Title == other.Title && Summary == other.Summary &&
            Error == other.Error && Heading == other.Heading && Problem == other.Problem &&
            Phases.SequenceEqual(other.Phases);

        public static ProjectRow From(NovelStatus s)
        {
            // Loading is words (DESIGN.md §5): one readable sentence for the active stage — and, for the
            // stages that walk a list, the running count so it's clear how much is left.
            var line = DeskStatus.Of(s);
            var summary = line.Summary;

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
            AddChip(chips, "analysis", s.AnalysisNotes, s.Stage == "ComposingAnalysis" && !Complete(s.AnalysisNotes));
            if (s.Reads is { } reads)
                AddChip(chips, "reads", reads, s.Stage == "ComposingAnalysis" && !line.Stuck);

            var showError = s.NeedsAttention;
            return new ProjectRow(
                s.Id, s.Title, summary, showError ? s.LastError : null, showError, chips, line.Heading, line.Problem);
        }

        private static string Approximately(TimeSpan duration) => duration.TotalDays switch
        {
            >= 14 => $"{Math.Round(duration.TotalDays / 7)} weeks",
            >= 2 => $"{Math.Round(duration.TotalDays)} days",
            >= 1 => "a day",
            >= 0.08 => $"{Math.Max(1, Math.Round(duration.TotalHours))} hours",
            _ => "a few minutes",
        };

        private static bool Complete(PhaseProgress phase) => phase.Total > 0 && phase.Done >= phase.Total;

        private static void AddChip(List<PhaseChipRow> chips, string name, PhaseProgress phase, bool active)
        {
            if (phase.Total > 0)
                chips.Add(new PhaseChipRow($"{name} {phase.Done} of {phase.Total}", active));
        }
    }
}
