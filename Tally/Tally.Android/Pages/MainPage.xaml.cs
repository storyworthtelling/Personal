using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Tally.Domain.Model;
using Tally.Domain.Time;
using Tally.Infrastructure.Services;

namespace Tally.Android.Pages;

public partial class MainPage : ContentPage
{
    private readonly AccountService accounts;

    public MainPage(AccountService accounts)
    {
        InitializeComponent();
        this.accounts = accounts;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
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
                    .Select(AccountRow.From)
                    .ToList()
            };
        }
    }

    public sealed class AccountRow
    {
        public string Title { get; init; } = string.Empty;
        public string AmountText { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public bool HasDetail => Detail.Length > 0;

        public static AccountRow From(Account account)
        {
            var title = string.IsNullOrEmpty(account.Mask) ? account.Name : $"{account.Name} ••{account.Mask}";
            var amount = account.CurrentBalance is { } current ? Money(current, account.IsoCurrencyCode) : "—";

            var parts = new List<string>();
            if (account.Kind == AccountKind.Depository && account.AvailableBalance is { } available)
                parts.Add($"{Money(available, account.IsoCurrencyCode)} available");
            if (account.Kind == AccountKind.Credit && account.CreditLimit is { } limit)
                parts.Add($"of {Money(limit, account.IsoCurrencyCode)} limit");
            if (account.NextPaymentDueDate is { } due)
            {
                var payment = account.NextPaymentAmount is { } amt ? $" · {Money(amt, account.IsoCurrencyCode)}" : "";
                parts.Add($"due {TimeFormats.Stamp(due.LocalDateTime).Split(',')[0]}{payment}");
            }
            if (account.Apr is { } apr)
                parts.Add($"{apr:0.##}% APR");

            return new AccountRow { Title = title, AmountText = amount, Detail = string.Join(" · ", parts) };
        }
    }
}
