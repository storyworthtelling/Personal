using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Ledger.Domain.External;
using Ledger.Domain.Model;
using Ledger.Domain.Time;
using Ledger.Infrastructure.Services;
using Storyworthtelling.Voice;

namespace Ledger.Android.Pages;

public partial class MainPage : ContentPage
{
    private readonly AccountService accounts;
    private readonly IPlaidClient plaid;
    private readonly DriveVoiceOptions voiceOptions;
    private readonly VoicedMessenger voice;

    public MainPage(AccountService accounts, IPlaidClient plaid, DriveVoiceOptions voiceOptions, VoicedMessenger voice)
    {
        InitializeComponent();
        this.accounts = accounts;
        this.plaid = plaid;
        this.voiceOptions = voiceOptions;
        this.voice = voice;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
        _ = ShowWelcomeAsync();
    }

    // A greeting composed fresh in the owner's voice, shown only once a shared voice profile exists.
    // Pulls the Claude key from the shared Drive config first (RefreshAsync); everything degrades to a
    // hidden label when the profile, key, or Drive access isn't there — never blocks or crashes the page.
    private async Task ShowWelcomeAsync()
    {
        if (string.IsNullOrEmpty(SettingsStore.GoogleAccountEmail))
        {
            WelcomeLabel.IsVisible = false;
            return;
        }

        string message;
        try
        {
            await voiceOptions.RefreshAsync(CancellationToken.None);
            var context = "the owner is checking their balances in Ledger";
            message = await voice.ComposeAsync(new MessageRequest("welcome", context), fallback: string.Empty, CancellationToken.None);
        }
        catch
        {
            message = string.Empty;
        }

        WelcomeLabel.Text = message;
        WelcomeLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private async Task ReloadAsync()
    {
        ConfigWarningCard.IsVisible = !SettingsStore.IsConfigured;
        LastRunLabel.Text = SettingsStore.LastRunSummary;

        var institutions = await accounts.GetInstitutionsAsync(CancellationToken.None);
        var rows = institutions.Select(InstitutionRow.From).ToList();
        InstitutionsList.ItemsSource = rows;

        UpdateNet(institutions);
    }

    private void UpdateNet(IReadOnlyList<Institution> institutions)
    {
        var all = institutions.SelectMany(i => i.Accounts).ToList();
        if (all.Count == 0)
        {
            NetLabel.IsVisible = false;
            return;
        }

        var assets = all.Where(a => a.Kind is AccountKind.Depository or AccountKind.Investment)
            .Sum(a => a.CurrentBalance ?? 0);
        var owed = all.Where(a => a.Kind is AccountKind.Credit or AccountKind.Loan)
            .Sum(a => a.CurrentBalance ?? 0);
        var net = assets - owed;

        NetLabel.Text = $"Estimated net {Money(net, "USD")} · {all.Count} accounts";
        NetLabel.IsVisible = true;
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        if (!SettingsStore.IsConfigured)
        {
            ConfigWarningCard.IsVisible = true;
            return;
        }

        // Success is an inline state change (DESIGN.md §7): mark everything due, kick the worker, and
        // let the status line speak — no toast.
        await accounts.MarkAllDueAsync(CancellationToken.None);
        RefreshWorker.RunNow(global::Android.App.Application.Context!);
        LastRunLabel.Text = "Refreshing…";
        await ReloadAsync();
    }

    private void OnSettingsClicked(object? sender, EventArgs e) =>
        Navigation.PushAsync(IPlatformApplication.Current!.Services.GetRequiredService<SetupPage>());

    // Expands/collapses an account's recent-transactions panel, loading it the first time.
    private async void OnAccountTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject { BindingContext: AccountRow row })
            return;

        row.IsExpanded = !row.IsExpanded;
        if (row.IsExpanded && !row.TransactionsLoaded)
            await LoadTransactionsAsync(row);
    }

    private async Task LoadTransactionsAsync(AccountRow row)
    {
        row.TransactionsLoaded = true;
        row.TransactionsStatus = "Loading recent transactions…";
        try
        {
            var end = DateOnly.FromDateTime(DateTime.Today);
            var start = end.AddDays(-30);
            var transactions = await plaid.GetTransactionsAsync(
                row.AccessToken, new[] { row.PlaidAccountId }, start, end, 15, CancellationToken.None);

            row.Transactions.Clear();
            foreach (var transaction in transactions.OrderByDescending(t => t.Date).Take(15))
                row.Transactions.Add(TxnRow.From(transaction));

            row.TransactionsStatus = row.Transactions.Count == 0 ? "No recent transactions." : string.Empty;
        }
        catch (Exception ex)
        {
            row.TransactionsLoaded = false; // allow another tap to retry
            row.TransactionsStatus = ex.Message.Contains("NOT_READY", StringComparison.OrdinalIgnoreCase)
                ? "Transactions aren't ready yet — try again in a minute."
                : "Couldn't load transactions. Pull Refresh, then try again.";
        }
    }

    // Formats a signed money amount in the account's currency; USD renders as "$1,234.56".
    internal static string Money(double value, string? isoCurrencyCode)
    {
        var code = string.IsNullOrEmpty(isoCurrencyCode) ? "USD" : isoCurrencyCode;
        var text = value.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        return code == "USD" ? text : $"{code} {text}";
    }

    public sealed class InstitutionRow
    {
        public string Name { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public bool HasError { get; init; }
        public IReadOnlyList<AccountRow> Accounts { get; init; } = [];

        public static InstitutionRow From(Institution institution)
        {
            var failed = institution.RefreshStatus == WorkStatus.Failed;
            string status;
            if (failed)
                status = institution.LastError ?? "Couldn't refresh — reconnect in Settings.";
            else if (institution.LastRefreshedUtc is { } refreshed)
                status = "Updated " + RelativeTime.Format(refreshed.LocalDateTime, DateTime.Now);
            else
                status = "Refreshing…";

            return new InstitutionRow
            {
                Name = institution.Name,
                HasError = failed,
                Status = status,
                Accounts = institution.Accounts
                    .OrderBy(a => a.Kind)
                    .ThenBy(a => a.Name)
                    .Select(a => AccountRow.From(a, institution.AccessToken))
                    .ToList()
            };
        }
    }

    // Mutable so tapping can expand it and lazy-load transactions in place.
    public sealed class AccountRow : INotifyPropertyChanged
    {
        private bool isExpanded;
        private string transactionsStatus = string.Empty;

        public string Title { get; init; } = string.Empty;
        public string AmountText { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public bool HasDetail => Detail.Length > 0;

        // Needed to fetch this account's transactions on demand.
        public string AccessToken { get; init; } = string.Empty;
        public string PlaidAccountId { get; init; } = string.Empty;

        public ObservableCollection<TxnRow> Transactions { get; } = [];
        public bool TransactionsLoaded { get; set; }

        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                if (isExpanded == value) return;
                isExpanded = value;
                Raise(nameof(IsExpanded));
                Raise(nameof(ExpandGlyph));
            }
        }

        // A small chevron cue; no image asset needed.
        public string ExpandGlyph => isExpanded ? "▾" : "▸";

        public string TransactionsStatus
        {
            get => transactionsStatus;
            set
            {
                if (transactionsStatus == value) return;
                transactionsStatus = value;
                Raise(nameof(TransactionsStatus));
                Raise(nameof(HasTransactionsStatus));
            }
        }

        public bool HasTransactionsStatus => transactionsStatus.Length > 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public static AccountRow From(Account account, string accessToken)
        {
            var title = string.IsNullOrEmpty(account.Mask) ? account.Name : $"{account.Name} ••{account.Mask}";
            var amount = account.CurrentBalance is { } current ? Money(current, account.IsoCurrencyCode) : "—";

            var parts = new List<string>();
            if (account.AvailableBalance is { } available)
                parts.Add($"{Money(available, account.IsoCurrencyCode)} available");
            if (account.CreditLimit is { } limit)
                parts.Add($"{Money(limit, account.IsoCurrencyCode)} limit");
            if (account.NextPaymentDueDate is { } due)
            {
                var payment = account.NextPaymentAmount is { } amt ? $" · {Money(amt, account.IsoCurrencyCode)}" : "";
                parts.Add($"due {TimeFormats.Stamp(due.LocalDateTime).Split(',')[0]}{payment}");
            }
            if (account.Apr is { } apr)
                parts.Add($"{apr:0.##}% APR");

            return new AccountRow
            {
                Title = title,
                AmountText = amount,
                Detail = string.Join(" · ", parts),
                AccessToken = accessToken,
                PlaidAccountId = account.AccountId
            };
        }
    }

    public sealed class TxnRow
    {
        public string Title { get; init; } = string.Empty;
        public string DateText { get; init; } = string.Empty;
        public string AmountText { get; init; } = string.Empty;

        public static TxnRow From(PlaidTransaction transaction)
        {
            var name = string.IsNullOrEmpty(transaction.MerchantName) ? transaction.Name : transaction.MerchantName!;
            var when = transaction.Date.LocalDateTime.ToString("MMM d", CultureInfo.InvariantCulture);
            if (transaction.Pending)
                when += " · pending";
            return new TxnRow
            {
                Title = name,
                DateText = when,
                AmountText = Money(transaction.Amount, transaction.IsoCurrencyCode)
            };
        }
    }
}
