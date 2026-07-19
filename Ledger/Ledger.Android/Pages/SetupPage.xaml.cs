using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Ledger.Domain.External;
using Ledger.Domain.Model;
using Ledger.Domain.Time;
using Ledger.Infrastructure.Services;

namespace Ledger.Android.Pages;

public partial class SetupPage : ContentPage
{
    private readonly AccountService accounts;
    private readonly IPlaidClient plaid;

    private List<InstitutionRow> rows = [];
    private InstitutionRow? pendingRemoval;
    private IDispatcherTimer? undoTimer;

    public SetupPage(AccountService accounts, IPlaidClient plaid)
    {
        InitializeComponent();
        this.accounts = accounts;
        this.plaid = plaid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        ClientIdEntry.Text = SettingsStore.PlaidClientId;
        SecretEntry.Text = SettingsStore.PlaidSecret;
        EnvironmentPicker.SelectedItem = SettingsStore.PlaidEnvironment;

        var migrationError = SettingsStore.LastMigrationError;
        MigrationErrorCard.IsVisible = !string.IsNullOrEmpty(migrationError);
        MigrationErrorLabel.Text = migrationError;

        await ReloadConnectedAsync();
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

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        SettingsStore.PlaidClientId = ClientIdEntry.Text?.Trim() ?? string.Empty;
        SettingsStore.PlaidSecret = SecretEntry.Text?.Trim() ?? string.Empty;
        SettingsStore.PlaidEnvironment = (EnvironmentPicker.SelectedItem as string) ?? "sandbox";
        // Success is a state change, not a toast (DESIGN.md §7): return to the list.
        Navigation.PopAsync();
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
