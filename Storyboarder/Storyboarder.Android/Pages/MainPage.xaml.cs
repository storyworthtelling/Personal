using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Storyboarder.Domain.Time;
using Storyboarder.Infrastructure.Services;
using Storyworthtelling.Voice;

namespace Storyboarder.Android.Pages;

public partial class MainPage : ContentPage
{
    private readonly ObservableCollection<ProjectRow> rows = [];
    private readonly Dictionary<Guid, NovelStatus> statuses = [];
    private IDispatcherTimer? refreshTimer;

    public MainPage()
    {
        InitializeComponent();
        ProjectsList.ItemsSource = rows;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
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

            var updated = list.Select(ProjectRow.From).ToList();
            for (var i = rows.Count - 1; i >= 0; i--)
            {
                if (updated.All(row => row.Id != rows[i].Id))
                    rows.RemoveAt(i);
            }
            for (var i = 0; i < updated.Count; i++)
            {
                var existing = rows.FirstOrDefault(row => row.Id == updated[i].Id);
                if (existing is null)
                    rows.Insert(Math.Min(i, rows.Count), updated[i]);
                else if (!existing.SameAs(updated[i]))
                    rows[rows.IndexOf(existing)] = updated[i];
            }
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
            WelcomeLabel.IsVisible = false;
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

        WelcomeLabel.Text = message;
        WelcomeLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private async void OnProjectSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ProjectRow row)
            return;
        ProjectsList.SelectedItem = null;

        if (!statuses.TryGetValue(row.Id, out var status))
            return;

        var buttons = new List<string>();

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

        var isActive = status.Stage is not ("Completed" or "Failed" or "Cancelled");
        if (isActive)
        {
            buttons.Add(RetryAction);
            buttons.Add(StopAction);
        }

        var choice = await DisplayActionSheet(status.Title, "Cancel", DeleteAction, buttons.ToArray());

        switch (choice)
        {
            case DeleteAction: await DeleteProject(status); break;
            case OpenReportPdfAction: await ShareReportPdf(status); break;
            case ReadReportAction: await OpenReport(status); break;
            case NewDraftAction: await StartNewDraft(status); break;
            case DiffAction: await ShowDraftDiff(status); break;
            case RetryAction: await RetryProject(status); break;
            case StopAction: await StopProject(status); break;
        }
    }

    private const string RetryAction = "Retry now";
    private const string StopAction = "Stop review";
    private const string DeleteAction = "Delete manuscript";
    private const string NewDraftAction = "New draft (re-pick doc)";
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
        Storyboarder.Domain.Analysis.BookAnalysisResult? report;
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

    // Shows what the last revision moved: the report findings it resolved and newly raised.
    private async Task ShowDraftDiff(NovelStatus status)
    {
        Storyboarder.Domain.Analysis.DraftDiff diff;
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
