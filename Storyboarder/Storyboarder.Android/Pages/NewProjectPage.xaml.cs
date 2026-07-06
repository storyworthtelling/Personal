using Microsoft.Extensions.DependencyInjection;
using Storyboarder.Android.Google;
using Storyboarder.Domain.Model;
using Storyboarder.Infrastructure.Services;

namespace Storyboarder.Android.Pages;

public partial class NewProjectPage : ContentPage
{
    private readonly DeviceAccountTokenProvider tokens;
    private readonly DriveDocsLister lister;

    // The current folder breadcrumb (bottom = current); starts at My Drive root.
    private readonly List<(string Id, string Name)> path = [];
    private DriveDocsLister.DriveEntry? selected;

    public NewProjectPage(DeviceAccountTokenProvider tokens, DriveDocsLister lister)
    {
        InitializeComponent();
        this.tokens = tokens;
        this.lister = lister;
        ArtStyleEntry.Text = ArtStyles.Default;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateAccountLabel();
        if (SettingsStore.IsGoogleConfigured && path.Count == 0)
            _ = ResetToRootAsync();
    }

    private void UpdateAccountLabel() =>
        AccountLabel.Text = SettingsStore.IsGoogleConfigured
            ? $"Signed in as {SettingsStore.GoogleAccountEmail}"
            : "Not signed in";

    private async void OnSignInClicked(object sender, EventArgs e)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            await DisplayAlert("Google", "Could not start sign-in (no active window).", "OK");
            return;
        }

        try
        {
            await DeviceAccountSignIn.SignIn(activity);
            UpdateAccountLabel();
            await ResetToRootAsync();
        }
        catch (Exception ex)
        {
            await Clipboard.SetTextAsync(ex.ToString());
            await DisplayAlert("Sign-in failed (copied to clipboard)", ex.Message, "OK");
        }
    }

    private async void OnRefreshClicked(object sender, EventArgs e) => await LoadCurrentFolderAsync();

    private async void OnUpClicked(object sender, EventArgs e)
    {
        if (path.Count <= 1)
            return;
        path.RemoveAt(path.Count - 1);
        await LoadCurrentFolderAsync();
    }

    private Task ResetToRootAsync()
    {
        path.Clear();
        path.Add(("root", "My Drive"));
        return LoadCurrentFolderAsync();
    }

    private async Task LoadCurrentFolderAsync()
    {
        if (!SettingsStore.IsGoogleConfigured)
        {
            await DisplayAlert("Google", "Sign in with Google first.", "OK");
            return;
        }

        selected = null;
        CreateButton.IsEnabled = false;
        UpButton.IsEnabled = path.Count > 1;
        PathLabel.Text = string.Join(" / ", path.Select(p => p.Name));

        Busy(true, "Loading My Drive…");
        try
        {
            var token = await tokens.GetAccessToken();
            var entries = await lister.ListChildrenAsync(token, path[^1].Id, CancellationToken.None);
            DocsList.ItemsSource = entries;
            StatusLabel.Text = entries.Count == 0 ? "Empty folder." : $"{entries.Count} item(s).";
        }
        catch (Exception ex)
        {
            await Clipboard.SetTextAsync(ex.ToString());
            await DisplayAlert("Could not list Drive (copied to clipboard)", ex.Message, "OK");
        }
        finally
        {
            Busy(false);
        }
    }

    private async void OnEntrySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not DriveDocsLister.DriveEntry entry)
            return;
        DocsList.SelectedItem = null; // we manage selection ourselves; clear the row highlight

        if (entry.IsFolder)
        {
            path.Add((entry.Id, entry.Name));
            await LoadCurrentFolderAsync();
        }
        else
        {
            selected = entry;
            CreateButton.IsEnabled = true;
            StatusLabel.Text = $"Selected: {entry.Name}";
        }
    }

    private async void OnCreateClicked(object sender, EventArgs e)
    {
        if (selected is null)
            return;

        var artStyle = string.IsNullOrWhiteSpace(ArtStyleEntry.Text)
            ? ArtStyles.Default
            : ArtStyleEntry.Text.Trim();

        Busy(true, "Creating project…");
        try
        {
            var token = await tokens.GetAccessToken();
            using (var scope = IPlatformApplication.Current!.Services.CreateScope())
            {
                var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
                _ = await projects.CreateFromDocAsync(
                    selected.Id, selected.Name, token, null, artStyle, CancellationToken.None);
            }

            // Kick off an immediate drain rather than waiting for the next periodic window.
            PipelineWorker.RunNow(global::Android.App.Application.Context);

            // Success is the state change (DESIGN.md §7): the library now
            // shows the new card with live progress chips.
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await Clipboard.SetTextAsync(ex.ToString());
            await DisplayAlert("Could not create project (copied to clipboard)", ex.Message, "OK");
        }
        finally
        {
            Busy(false);
        }
    }

    private void Busy(bool busy, string? message = null)
    {
        StatusLabel.Text = message ?? string.Empty;
        SignInButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        UpButton.IsEnabled = !busy && path.Count > 1;
        CreateButton.IsEnabled = !busy && selected is not null;
    }
}
