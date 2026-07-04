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
        LastRunLabel.Text = SettingsStore.LastRunResult;

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
            rows.Clear();
            foreach (var status in list)
            {
                statuses[status.Id] = status;
                rows.Add(ProjectRow.From(status));
            }
            LastRunLabel.Text = SettingsStore.LastRunResult;
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
            await DisplayAlert(row.Title, "No finished PDFs yet.", "OK");
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

    // Display projection of a NovelStatus for the list. Public so the XAML binding can read it.
    public sealed record ProjectRow(Guid Id, string Title, string Summary, string? Error, bool HasError)
    {
        public static ProjectRow From(NovelStatus s)
        {
            // One readable progress sentence: the stage plus only the phases
            // that have work, e.g. "Storyboarding — panels 12 of 40".
            var parts = new List<string>();
            AddPart(parts, "panels", s.Panels);
            AddPart(parts, "pages", s.Pages);
            AddPart(parts, "volumes", s.Volumes);
            AddPart(parts, "analysis", s.Analysis);
            var summary = parts.Count > 0 ? $"{s.Stage} — {string.Join(", ", parts)}" : s.Stage;
            return new ProjectRow(s.Id, s.Title, summary, s.LastError, !string.IsNullOrEmpty(s.LastError));
        }

        private static void AddPart(List<string> parts, string name, PhaseProgress phase)
        {
            if (phase.Total > 0)
            {
                parts.Add($"{name} {phase.Done} of {phase.Total}");
            }
        }
    }
}
