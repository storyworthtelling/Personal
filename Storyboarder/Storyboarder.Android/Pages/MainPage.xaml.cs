using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
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

        if (options.Count == 0)
        {
            // Nothing to open yet; the card's chips already say why.
            return;
        }

        var choice = await DisplayActionSheet("Open a PDF", "Cancel", null, options.Select(o => o.Label).ToArray());
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
                _ => s.Stage,
            };

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

            return new ProjectRow(
                s.Id, s.Title, summary, s.LastError, !string.IsNullOrEmpty(s.LastError),
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
