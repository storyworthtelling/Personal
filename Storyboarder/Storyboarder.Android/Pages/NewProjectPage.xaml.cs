using Microsoft.Extensions.DependencyInjection;
using Storyboarder.Android.Google;
using Storyboarder.Domain.Model;
using Storyboarder.Infrastructure.Services;

namespace Storyboarder.Android.Pages;

public partial class NewProjectPage : ContentPage
{
    private readonly DeviceAccountTokenProvider tokens;
    private readonly DriveDocsLister lister;
    private DriveDocsLister.DocItem? selected;

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
        if (SettingsStore.IsGoogleConfigured && DocsList.ItemsSource is null)
            _ = LoadDocsAsync();
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
            await LoadDocsAsync();
        }
        catch (Exception ex)
        {
            await Clipboard.SetTextAsync(ex.ToString());
            await DisplayAlert("Sign-in failed (copied to clipboard)", ex.Message, "OK");
        }
    }

    private async void OnRefreshDocsClicked(object sender, EventArgs e) => await LoadDocsAsync();

    private async Task LoadDocsAsync()
    {
        if (!SettingsStore.IsGoogleConfigured)
        {
            await DisplayAlert("Google", "Sign in with Google first.", "OK");
            return;
        }

        Busy(true, "Loading your Google Docs…");
        try
        {
            var token = await tokens.GetAccessToken();
            var docs = await lister.ListAsync(token, CancellationToken.None);
            DocsList.ItemsSource = docs;
            StatusLabel.Text = docs.Count == 0 ? "No Google Docs found." : $"{docs.Count} doc(s).";
        }
        catch (Exception ex)
        {
            await Clipboard.SetTextAsync(ex.ToString());
            await DisplayAlert("Could not list docs (copied to clipboard)", ex.Message, "OK");
        }
        finally
        {
            Busy(false);
        }
    }

    private void OnDocSelected(object? sender, SelectionChangedEventArgs e)
    {
        selected = e.CurrentSelection.FirstOrDefault() as DriveDocsLister.DocItem;
        CreateButton.IsEnabled = selected is not null;
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
                    selected.FileId, selected.Name, token, null, artStyle, CancellationToken.None);
            }

            // Kick off an immediate drain rather than waiting for the next periodic window.
            PipelineWorker.RunNow(global::Android.App.Application.Context);

            await DisplayAlert("Project created",
                $"“{selected.Name}” is queued. Rendering runs in the background.", "OK");
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
        Spinner.IsVisible = busy;
        Spinner.IsRunning = busy;
        StatusLabel.Text = message ?? string.Empty;
        SignInButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        CreateButton.IsEnabled = !busy && selected is not null;
    }
}
