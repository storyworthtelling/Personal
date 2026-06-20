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
        ConfigWarningLabel.IsVisible = !SettingsStore.IsConfigured;
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

        var ready = status.VolumeList.Where(v => v.PdfReady).ToList();
        if (ready.Count == 0)
        {
            await DisplayAlert(row.Title, "No finished volume PDFs yet.", "OK");
            return;
        }

        var labels = ready.Select(v => $"Volume {v.Number}: {v.Title}").ToArray();
        var choice = await DisplayActionSheet("Open a finished volume", "Cancel", null, labels);
        var picked = ready.FirstOrDefault(v => $"Volume {v.Number}: {v.Title}" == choice);
        if (picked is null)
            return;

        string? path;
        using (var scope = IPlatformApplication.Current!.Services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            path = await projects.GetVolumePdfPathAsync(status.Id, picked.Number, CancellationToken.None);
        }

        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            await DisplayAlert("Volume", "The PDF file could not be found.", "OK");
            return;
        }

        await Share.RequestAsync(new ShareFileRequest
        {
            Title = $"{status.Title} — Volume {picked.Number}",
            File = new ShareFile(path)
        });
    }

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
            var summary =
                $"{s.Stage} · panels {s.Panels.Done}/{s.Panels.Total} · " +
                $"pages {s.Pages.Done}/{s.Pages.Total} · volumes {s.Volumes.Done}/{s.Volumes.Total}";
            return new ProjectRow(s.Id, s.Title, summary, s.LastError, !string.IsNullOrEmpty(s.LastError));
        }
    }
}
