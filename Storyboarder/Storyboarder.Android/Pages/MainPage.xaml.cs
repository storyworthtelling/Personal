using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Storyboarder.Domain.Time;
using Storyboarder.Infrastructure.Services;

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

            // Diff-update so the 5-second refresh never flickers thumbnails.
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

    private async void OnProjectSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ProjectRow row)
            return;
        ProjectsList.SelectedItem = null;

        if (!statuses.TryGetValue(row.Id, out var status))
            return;

        // Openable PDFs: the finished comic volumes plus the book-analysis (revision) report.
        var options = new List<PdfChoice>();
        foreach (var volume in status.VolumeList.Where(v => v.PdfReady))
        {
            var number = volume.Number;
            options.Add(new PdfChoice(
                $"Volume {number}: {volume.Title}",
                $"{status.Title} — Volume {number}",
                projects => projects.GetVolumePdfPathAsync(status.Id, number, CancellationToken.None)));
        }
        if (status.AnalysisReady)
            options.Add(new PdfChoice(
                "Analysis report (revision notes)",
                $"{status.Title} — Analysis",
                projects => projects.GetAnalysisPdfPathAsync(status.Id, CancellationToken.None)));

        // A project card always offers management (retry/stop/delete); PDFs appear once they're ready.
        var isActive = status.Stage is not ("Completed" or "Failed" or "Cancelled");
        var buttons = options.Select(o => o.Label).ToList();
        // Panels safety-blocked mid-render wait on the user's OK before the book can finish.
        var reviewLabel = status.PendingApprovals > 0 ? $"Review blocked panels ({status.PendingApprovals})" : null;
        if (reviewLabel is not null)
            buttons.Add(reviewLabel);
        // A book the pipeline cancelled on a prohibited/IP block carries a reason (a user-stopped one
        // doesn't): offer to rework that panel's art direction and resume, rather than restart the whole
        // conversion and discard the panels already rendered.
        var reworkable = status.Stage == "Cancelled" && !string.IsNullOrEmpty(status.LastError);
        if (reworkable)
            buttons.Add(ReworkPanelAction);
        if (isActive)
        {
            buttons.Add(RetryAction);
            buttons.Add(StopAction);
        }

        var choice = await DisplayActionSheet(status.Title, "Cancel", DeleteAction, buttons.ToArray());

        if (choice == DeleteAction)
        {
            await DeleteProject(status);
            return;
        }
        if (choice == reviewLabel)
        {
            await ReviewBlockedPanels(status);
            return;
        }
        if (choice == ReworkPanelAction)
        {
            await ReworkBlockedPanel(status);
            return;
        }
        if (choice == RetryAction)
        {
            await RetryProject(status);
            return;
        }
        if (choice == StopAction)
        {
            await StopProject(status);
            return;
        }

        var picked = options.FirstOrDefault(o => o.Label == choice);
        if (picked is null)
            return;

        string? path;
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            path = await picked.Resolve(projects);
        }

        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            await DisplayAlert("PDF", "The PDF file could not be found.", "OK");
            return;
        }

        await Share.RequestAsync(new ShareFileRequest
        {
            Title = picked.ShareTitle,
            File = new ShareFile(path)
        });
    }

    private const string RetryAction = "Retry now";
    private const string StopAction = "Stop conversion";
    private const string DeleteAction = "Delete project";
    private const string ApprovePanelAction = "Render it (relax this once)";
    private const string SkipPanelAction = "Leave it out";
    private const string ReworkPanelAction = "Rework blocked panel & resume";

    // Walks the panels a safety filter blocked mid-render, one at a time. For each, the user can approve
    // rendering it (the flagged category is relaxed for that one render — and, if it still hits the
    // model's floor, Claude restages the prompt to imply the content) or skip it (the page is drawn
    // without it). Either choice kicks the worker so the decision takes effect at once.
    private async Task ReviewBlockedPanels(NovelStatus status)
    {
        IReadOnlyList<BlockedPanel> blocked;
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            blocked = await projects.GetBlockedPanelsAsync(status.Id, CancellationToken.None);
        }

        foreach (var panel in blocked)
        {
            var beat = FirstNonEmpty(panel.Dialogue, panel.Caption, panel.VisualPrompt) ?? "(no text)";
            var categories = panel.Categories.Count > 0
                ? string.Join(", ", panel.Categories.Select(FriendlyCategory))
                : "content";
            var choice = await DisplayActionSheet(
                $"Panel {panel.PanelNumber} — blocked as {categories}: “{Shorten(beat)}”",
                "Decide later", null, ApprovePanelAction, SkipPanelAction);

            if (choice == ApprovePanelAction)
                await ResolvePanel(panel.PanelId, approve: true);
            else if (choice == SkipPanelAction)
                await ResolvePanel(panel.PanelId, approve: false);
            else
                break; // "Decide later" (or dismissed) — leave the rest parked.
        }

        await RefreshAsync();
    }

    private async Task ResolvePanel(Guid panelId, bool approve)
    {
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            if (approve)
                await projects.ApprovePanelRenderAsync(panelId, CancellationToken.None);
            else
                await projects.SkipPanelRenderAsync(panelId, CancellationToken.None);
        }
        PipelineWorker.RunNow(global::Android.App.Application.Context);
    }

    // The pipeline cancelled this book on a panel Gemini blocked as prohibited/copyrighted content — no
    // safety threshold can bypass that. Rather than restart, let the user revise that panel's art
    // direction (prefilled with what was sent) and resume: the panel re-renders with the new prompt and
    // the book carries on from where it stopped, keeping every panel already rendered. The card moving
    // Cancelled → "Painting panels" is the confirmation (DESIGN.md §5 — no success toast).
    private async Task ReworkBlockedPanel(NovelStatus status)
    {
        ProhibitedPanel? panel;
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            panel = await projects.GetProhibitedPanelAsync(status.Id, CancellationToken.None);
        }
        if (panel is null)
        {
            // Nothing left to rework (already resumed elsewhere, or state moved on) — just refresh.
            await RefreshAsync();
            return;
        }

        var newPrompt = await DisplayPromptAsync(
            $"Rework panel {panel.PanelNumber}",
            "Gemini blocked this panel as prohibited or copyrighted content. Revise the art direction to " +
            "avoid it, then resume — the rest of the book is kept.",
            accept: "Resume", cancel: "Cancel",
            initialValue: panel.VisualPrompt, maxLength: 2000, keyboard: Keyboard.Text);
        if (string.IsNullOrWhiteSpace(newPrompt))
            return;

        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            await projects.ReworkBlockedPanelAndResumeAsync(status.Id, newPrompt.Trim(), CancellationToken.None);
        }
        PipelineWorker.RunNow(global::Android.App.Application.Context);
        await RefreshAsync();
    }

    // Turns a Gemini HARM_CATEGORY_* name into a short phrase for the review prompt.
    private static string FriendlyCategory(string category) => category switch
    {
        "HARM_CATEGORY_DANGEROUS_CONTENT" => "violence / dangerous",
        "HARM_CATEGORY_SEXUALLY_EXPLICIT" => "sexual",
        "HARM_CATEGORY_HARASSMENT" => "harassment",
        "HARM_CATEGORY_HATE_SPEECH" => "hate",
        _ => "content"
    };

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string Shorten(string text, int max = 80) =>
        text.Length <= max ? text : text[..max] + "…";

    // Clears the stall and kicks the pipeline immediately, rather than waiting for the next periodic
    // window. Resets the current stage's stuck work to due-now, then fires an off-cycle drain.
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

    // Stops a running conversion but keeps the card and any finished PDFs.
    private async Task StopProject(NovelStatus status)
    {
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            await projects.CancelAsync(status.Id, CancellationToken.None);
        }
        await RefreshAsync();
    }

    // Removes a project entirely (rows + rendered files) after a confirm. DESIGN.md §5 asks for an
    // undo bar; MAUI has no such component, so this mirrors the Restore flow's confirm dialog.
    private async Task DeleteProject(NovelStatus status)
    {
        var confirm = await DisplayAlert(
            "Delete this project?",
            $"“{status.Title}” and its rendered pages will be removed for good. This can’t be undone.",
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

    // One entry in the "open a PDF" action sheet: its label, share title, and how to resolve its path.
    private sealed record PdfChoice(string Label, string ShareTitle, Func<ProjectService, Task<string?>> Resolve);

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
        string? CoverThumb, bool HasCover, IReadOnlyList<PhaseChipRow> Phases)
    {
        // Record equality is referential for the Phases list; compare content
        // so the diff-refresh can skip rows that haven't changed.
        public bool SameAs(ProjectRow other) =>
            Id == other.Id && Title == other.Title && Summary == other.Summary &&
            Error == other.Error && CoverThumb == other.CoverThumb &&
            Phases.SequenceEqual(other.Phases);

        public static ProjectRow From(NovelStatus s)
        {
            // Loading is words (DESIGN.md §5): one readable sentence for the active stage — and,
            // for the stages that walk a list, the running count so it's clear how much is left.
            var summary = s.Stage switch
            {
                "Created" or "Ingesting" => "Reading the manuscript…",
                "BuildingDesignBible" => DesignBibleSentence(s),
                "Storyboarding" => Sentence("Storyboarding the book", "chunks", s.Storyboard),
                "RenderingImages" => Sentence("Painting panels", "panels", s.Panels),
                "ComposingPages" => Sentence("Composing pages", "pages", s.Pages),
                "AssemblingVolumes" => Sentence("Binding volumes", "volumes", s.Volumes),
                "ComposingAnalysis" => AnalysisSentence(s),
                "Completed" => "Finished.",
                "Failed" => "Stopped.",
                // A book the pipeline cancelled itself (prohibited/copyrighted image content) carries a
                // reason; a book the user stopped by hand does not.
                "Cancelled" => string.IsNullOrEmpty(s.LastError) ? "Stopped by you." : "Stopped — content couldn’t be rendered.",
                _ => s.Stage,
            };

            // When a provider rate-limited us, say when queries resume — in local 12-hour time, per
            // DESIGN.md §11. Only set while still in the future (the status read model gates it).
            if (s.RateLimitedUntilUtc is { } until)
            {
                var local = until.ToLocalTime().DateTime;
                var when = local.Date == DateTime.Now.Date ? TimeFormats.Clock12(local) : TimeFormats.Stamp(local);
                summary = $"{summary} · rate-limited, resumes ~{when}";
            }

            // The whole pipeline as a wrapping chip strip: each phase that has work shows its
            // done-of-total, and the stage in progress is highlighted (DESIGN.md §7).
            var chips = new List<PhaseChipRow>();
            AddChip(chips, "chunks", s.Chunks, s.Stage == "BuildingDesignBible" && !Complete(s.Chunks));
            AddChip(chips, "sheets", s.ReferenceSheets, s.Stage == "BuildingDesignBible" && Complete(s.Chunks));
            AddChip(chips, "storyboard", s.Storyboard, s.Stage == "Storyboarding");
            AddChip(chips, "panels", s.Panels, s.Stage == "RenderingImages");
            AddChip(chips, "pages", s.Pages, s.Stage == "ComposingPages");
            AddChip(chips, "volumes", s.Volumes, s.Stage == "AssemblingVolumes");
            AddChip(chips, "analysis", s.AnalysisNotes, s.Stage == "ComposingAnalysis");

            // Only surface the error (and the "needs attention" chip) when the pipeline has truly
            // stalled or is waiting on the user — a transient failure the worker is still auto-retrying
            // stays quiet so the card doesn't cry wolf while it self-heals. A self-cancelled book (its
            // content was blocked as prohibited) also shows its reason.
            var cancelledWithReason = s.Stage == "Cancelled" && !string.IsNullOrEmpty(s.LastError);
            var showError = s.NeedsAttention || cancelledWithReason;
            return new ProjectRow(
                s.Id, s.Title, summary, showError ? s.LastError : null, showError,
                s.CoverThumbPath, !string.IsNullOrEmpty(s.CoverThumbPath), chips);
        }

        // The design bible has two sub-steps: read every chunk for characters & settings, then draw
        // a reference sheet for each. Report whichever is in flight so the sentence never stalls.
        private static string DesignBibleSentence(NovelStatus s) =>
            Complete(s.Chunks)
                ? Sentence("Drawing reference sheets", "sheets", s.ReferenceSheets)
                : Sentence("Building the design bible", "chunks", s.Chunks);

        // The report maps every chapter into notes, then consolidates and renders it as a PDF.
        private static string AnalysisSentence(NovelStatus s) =>
            Complete(s.AnalysisNotes)
                ? "Writing the revision report…"
                : Sentence("Analysing the manuscript", "chapters", s.AnalysisNotes);

        private static bool Complete(PhaseProgress phase) => phase.Total > 0 && phase.Done >= phase.Total;

        private static string Sentence(string verb, string noun, PhaseProgress phase) =>
            phase.Total > 0 ? $"{verb} — {noun} {phase.Done} of {phase.Total}…" : $"{verb}…";

        private static void AddChip(List<PhaseChipRow> chips, string name, PhaseProgress phase, bool active)
        {
            if (phase.Total > 0)
            {
                chips.Add(new PhaseChipRow($"{name} {phase.Done} of {phase.Total}", active));
            }
        }
    }
}
