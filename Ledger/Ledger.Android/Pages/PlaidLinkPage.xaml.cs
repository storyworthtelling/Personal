using Ledger.Domain.External;
using Ledger.Infrastructure.Services;

namespace Ledger.Android.Pages;

// Connects an institution via Plaid Hosted Link in a WebView. The app creates the link token and
// hosts the returned URL; when the user finishes, "Done" reads the completed session's public_token
// (/link/token/get), exchanges it for a durable access token, and records the institution.
public partial class PlaidLinkPage : ContentPage
{
    private readonly IPlaidClient plaid;
    private readonly AccountService accounts;

    private string? linkToken;
    private bool finishing;

    public PlaidLinkPage(IPlaidClient plaid, AccountService accounts)
    {
        InitializeComponent();
        this.plaid = plaid;
        this.accounts = accounts;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (linkToken is not null)
            return; // already prepared

        try
        {
            var packageName = global::Android.App.Application.Context.PackageName ?? "com.storyworthtelling.ledger";
            var token = await plaid.CreateLinkTokenAsync(packageName, CancellationToken.None);
            linkToken = token.LinkToken;

            if (string.IsNullOrEmpty(token.HostedLinkUrl))
            {
                StatusLabel.Text = "Couldn't open Plaid Link. Check your credentials and environment in Settings.";
                return;
            }

            LinkWebView.Source = token.HostedLinkUrl;
            StatusLabel.Text = "Sign in to your institution above, then tap Done.";
            DoneButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Couldn't start Plaid Link: {ex.Message}";
        }
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        if (finishing || linkToken is null)
            return;
        finishing = true;
        DoneButton.IsEnabled = false;
        StatusLabel.Text = "Finishing up…";

        try
        {
            var result = await plaid.GetLinkResultAsync(linkToken, CancellationToken.None);
            if (result is null)
            {
                StatusLabel.Text = "Finish connecting in the page above, then tap Done.";
                DoneButton.IsEnabled = true;
                finishing = false;
                return;
            }

            var access = await plaid.ExchangePublicTokenAsync(result.PublicToken, CancellationToken.None);
            var name = result.InstitutionName ?? access.InstitutionName ?? "New account";
            await accounts.AddInstitutionAsync(access.ItemId, access.AccessToken, name, CancellationToken.None);

            // Kick an immediate refresh so balances appear without waiting for the daily cycle.
            RefreshWorker.RunNow(global::Android.App.Application.Context!);
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Couldn't finish connecting: {ex.Message}";
            DoneButton.IsEnabled = true;
            finishing = false;
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await Navigation.PopAsync();
}
