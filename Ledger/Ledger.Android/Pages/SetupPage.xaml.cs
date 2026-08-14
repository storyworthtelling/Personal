using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Ledger.Android.Google;
using Ledger.Domain.External;
using Ledger.Domain.Health;
using Ledger.Domain.Model;
using Ledger.Domain.Time;
using Ledger.Infrastructure.Services;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;

namespace Ledger.Android.Pages;

public partial class SetupPage : ContentPage
{
    private readonly AccountService accounts;
    private readonly IPlaidClient plaid;
    private readonly VaultSync vaultSync;

    private List<InstitutionRow> rows = [];
    private InstitutionRow? pendingRemoval;
    private IDispatcherTimer? undoTimer;

    public SetupPage(AccountService accounts, IPlaidClient plaid, VaultSync vaultSync)
    {
        InitializeComponent();
        this.accounts = accounts;
        this.plaid = plaid;
        this.vaultSync = vaultSync;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Pre-fill the (usually hidden) credential entries so a save never wipes them; ApplyPlaidCredentialField
        // then decides whether to show them based on the credentials' last-known health.
        ClientIdEntry.Text = SettingsStore.PlaidClientId;
        SecretEntry.Text = SettingsStore.PlaidSecret;
        ApplyPlaidCredentialField();
        EnvironmentPicker.SelectedItem = SettingsStore.PlaidEnvironment;
        VaultPassphraseEntry.Text = SettingsStore.VaultPassphrase;

        var migrationError = SettingsStore.LastMigrationError;
        MigrationErrorCard.IsVisible = !string.IsNullOrEmpty(migrationError);
        MigrationErrorLabel.Text = migrationError;

        await ReloadConnectedAsync();

        VoiceStatusLabel.Text = string.IsNullOrEmpty(SettingsStore.GoogleAccountEmail)
            ? string.Empty
            : $"Signed in as {SettingsStore.GoogleAccountEmail}.";
    }

    // Shows the Plaid client id/secret fields only when there's something to do: no credentials stored yet,
    // or Plaid just rejected them. Working credentials collapse to a "Working ✓" line with a Replace button,
    // so the screen stays quiet and the secret isn't left on display (the vault keeps the backup).
    private void ApplyPlaidCredentialField()
    {
        var isSet = !string.IsNullOrEmpty(SettingsStore.PlaidClientId) &&
                    !string.IsNullOrEmpty(SettingsStore.PlaidSecret);
        var state = CredentialFieldState.Decide(isSet, CredentialHealth().Get(CredentialIds.Plaid));
        PlaidCredentialFields.IsVisible = state.ShowEntry;
        PlaidStatusRow.IsVisible = !state.ShowEntry || state.IsWarning;
        PlaidReplaceButton.IsVisible = !state.ShowEntry;
        PlaidStatusLabel.Text = state.StatusText;
        PlaidStatusLabel.Style = LookupStyle(state.IsWarning ? "ErrorLabel" : "SecondaryLabel");
    }

    private static CredentialHealthReport CredentialHealth() =>
        IPlatformApplication.Current!.Services.GetRequiredService<CredentialHealthStore>().Load();

    // Styles live in the app-wide merged dictionaries, which a page's own Resources indexer doesn't search;
    // TryGetValue on the application resources walks the merged dictionaries.
    private static Style? LookupStyle(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true ? value as Style : null;

    // Deliberate rotation of working credentials: hide the status row and open the fields (kept empty so the
    // new values are typed fresh rather than edited over the old ones).
    private void OnReplacePlaidClicked(object? sender, EventArgs e)
    {
        PlaidStatusRow.IsVisible = false;
        ClientIdEntry.Text = string.Empty;
        SecretEntry.Text = string.Empty;
        PlaidCredentialFields.IsVisible = true;
        ClientIdEntry.Focus();
    }

    // Opens the system Google account picker and requests read-only Drive consent, so Ledger can read the
    // shared owner's-voice profile. The result returns through MainActivity.OnActivityResult.
    private async void OnVoiceSignInClicked(object sender, EventArgs e)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
            return;
        try
        {
            var email = await DeviceAccountSignIn.SignIn(activity);
            VoiceStatusLabel.Text = $"Signed in as {email}.";
        }
        catch (Exception ex)
        {
            VoiceStatusLabel.Text = $"Sign-in didn't complete. {ex.Message}";
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Don't leave a removal half-done if the user navigates away mid-undo.
        CommitPendingRemoval();
    }

    private async Task ReloadConnectedAsync()
    {
        var institutions = await accounts.GetInstitutionsAsync(CancellationToken.None);
        rows = institutions.Select(InstitutionRow.From).ToList();
        ConnectedList.BindingContext = rows;
        NoAccountsLabel.IsVisible = rows.Count == 0;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        SettingsStore.PlaidClientId = ClientIdEntry.Text?.Trim() ?? string.Empty;
        SettingsStore.PlaidSecret = SecretEntry.Text?.Trim() ?? string.Empty;
        SettingsStore.PlaidEnvironment = (EnvironmentPicker.SelectedItem as string) ?? "sandbox";
        SettingsStore.VaultPassphrase = VaultPassphraseEntry.Text ?? string.Empty;
        // Back up the Plaid credentials + connected banks to the encrypted Drive vault. The backup is
        // optional — the settings already work locally — but a failure now surfaces instead of vanishing: a
        // silently-skipped backup is exactly what left the vault empty after the cutover.
        await PublishToVaultAsync();
        // Success is a state change, not a toast (DESIGN.md §7): return to the list.
        await Navigation.PopAsync();
    }

    // Publishes and, only when a passphrase was set but the backup didn't land, tells the owner. A
    // no-passphrase skip stays quiet (the app just isn't in the vault yet).
    private async Task PublishToVaultAsync()
    {
        SecretsVaultPublishResult result;
        try { result = await vaultSync.PublishAsync(CancellationToken.None); }
        catch (Exception ex) { result = SecretsVaultPublishResult.WriteFailed(ex.Message); }
        if (result.NeedsAttention)
            await DisplayAlert("Vault backup didn't complete", result.Message, "OK");
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (!SettingsStore.IsConfigured)
        {
            ConnectStatusLabel.Text = "Enter your Plaid client ID and secret, then Save, before connecting.";
            return;
        }
        // Persist any just-typed credentials so the Link page uses them.
        SettingsStore.PlaidClientId = ClientIdEntry.Text?.Trim() ?? string.Empty;
        SettingsStore.PlaidSecret = SecretEntry.Text?.Trim() ?? string.Empty;
        SettingsStore.PlaidEnvironment = (EnvironmentPicker.SelectedItem as string) ?? "sandbox";

        await Navigation.PushAsync(IPlatformApplication.Current!.Services.GetRequiredService<PlaidLinkPage>());
    }

    private void OnRemoveClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: InstitutionRow row })
            return;

        // Any previous pending removal commits now; only one undo is in flight at a time.
        CommitPendingRemoval();

        rows.Remove(row);
        ConnectedList.BindingContext = rows.ToList();
        NoAccountsLabel.IsVisible = rows.Count == 0;

        pendingRemoval = row;
        UndoLabel.Text = $"Removed {row.Name}.";
        UndoBar.IsVisible = true;

        undoTimer = Dispatcher.CreateTimer();
        undoTimer.Interval = TimeSpan.FromMilliseconds(Ledger.Domain.Design.DesignTokens.Motion.UndoWindowMs);
        undoTimer.IsRepeating = false;
        undoTimer.Tick += (_, _) => CommitPendingRemoval();
        undoTimer.Start();
    }

    private void OnUndoClicked(object? sender, EventArgs e)
    {
        undoTimer?.Stop();
        undoTimer = null;
        pendingRemoval = null;
        UndoBar.IsVisible = false;
        _ = ReloadConnectedAsync();
    }

    private void CommitPendingRemoval()
    {
        undoTimer?.Stop();
        undoTimer = null;
        UndoBar.IsVisible = false;

        var row = pendingRemoval;
        pendingRemoval = null;
        if (row is null)
            return;

        // Best-effort release at Plaid (stops any server-side subscription), then delete locally.
        _ = Task.Run(async () =>
        {
            try { await plaid.RemoveItemAsync(row.AccessToken, CancellationToken.None); }
            catch { /* releasing at Plaid is best-effort; the local record still goes. */ }
            await accounts.RemoveInstitutionAsync(row.Id, CancellationToken.None);
            // Keep the vault's backup in step with the removal.
            try { await vaultSync.PublishAsync(CancellationToken.None); }
            catch { /* Best-effort backup. */ }
        });
    }

    private async void OnShareLogClicked(object? sender, EventArgs e)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, "refresh.log");
        if (!File.Exists(path))
        {
            DiagnosticsStatusLabel.Text = "No refresh log yet — connect an account and refresh first.";
            return;
        }
        await Share.RequestAsync(new ShareFileRequest
        {
            Title = "Ledger refresh log",
            File = new ShareFile(path)
        });
    }

    public sealed class InstitutionRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string AccessToken { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;

        public static InstitutionRow From(Institution institution)
        {
            var count = institution.Accounts.Count;
            var when = institution.LastRefreshedUtc is { } refreshed
                ? "updated " + RelativeTime.Format(refreshed.LocalDateTime, DateTime.Now)
                : "not refreshed yet";
            var accountsText = count == 1 ? "1 account" : $"{count} accounts";
            return new InstitutionRow
            {
                Id = institution.Id,
                Name = institution.Name,
                AccessToken = institution.AccessToken,
                Summary = $"{accountsText} · {when}"
            };
        }
    }
}
