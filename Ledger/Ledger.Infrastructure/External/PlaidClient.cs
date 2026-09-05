using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Ledger.Domain.External;
using Ledger.Domain.Health;
using Ledger.Domain.Pipeline;
using Ledger.Infrastructure.Configuration;
using Storyworthtelling.Voice.Health;

namespace Ledger.Infrastructure.External;

// The Plaid aggregator client. Every request posts a JSON body carrying the client_id/secret (Plaid
// authenticates in the body, not headers) to the environment host from PlaidOptions. Provider
// throttling (HTTP 429 / RATE_LIMIT_EXCEEDED) is surfaced as RateLimitException so the worker can
// reschedule instead of failing an institution outright.
public sealed class PlaidClient : IPlaidClient
{
    private readonly HttpClient httpClient;
    private readonly IOptions<PlaidOptions> options;
    private readonly ICredentialHealthSink? healthSink;

    public PlaidClient(
        HttpClient httpClient, IOptions<PlaidOptions> options, ICredentialHealthSink? healthSink = null)
    {
        this.httpClient = httpClient;
        this.options = options;
        this.healthSink = healthSink;
    }

    public async Task<PlaidLinkToken> CreateLinkTokenAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var payload = new
        {
            client_id = opts.ClientId,
            secret = opts.Secret,
            user = new { client_user_id = "ledger-owner" },
            client_name = "Ledger",
            language = "en",
            country_codes = new[] { "US" },
            // Plaid requires at least one product in `products` — an empty array is rejected with
            // INVALID_PRODUCT. Transactions is the app's broadest data product (it powers the recent-
            // transactions panel and is supported across banks and cards), so it is the required product;
            // Balance data comes free with any product, so balances work on every linked Item. Liabilities
            // is requested only where the institution supports it (a product can't appear in both lists),
            // so deposit-only institutions still link. Institutions that don't support Transactions (some
            // standalone loan/mortgage servicers) may not link — those fall back to manual awareness.
            products = new[] { "transactions" },
            required_if_supported_products = new[] { "liabilities" },
            // Hosted Link returns a URL the app opens in a WebView (no native Link SDK needed). Because this
            // is a hosted flow, android_package_name is deliberately omitted — sending it makes Plaid demand
            // that the package be registered in the developer dashboard (INVALID_FIELD) and switches OAuth to
            // a native app-to-app redirect the WebView can't complete.
            hosted_link = new { }
        };

        using var document = await PostAsync("/link/token/create", payload, cancellationToken);
        var root = document.RootElement;
        return new PlaidLinkToken(
            root.GetProperty("link_token").GetString() ?? string.Empty,
            OptionalString(root, "hosted_link_url"));
    }

    public async Task<PlaidLinkResult?> GetLinkResultAsync(string linkToken, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var payload = new { client_id = opts.ClientId, secret = opts.Secret, link_token = linkToken };

        using var document = await PostAsync("/link/token/get", payload, cancellationToken);
        if (!document.RootElement.TryGetProperty("link_sessions", out var sessions) ||
            sessions.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var session in sessions.EnumerateArray())
        {
            if (!session.TryGetProperty("results", out var results) ||
                !results.TryGetProperty("item_add_results", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in items.EnumerateArray())
            {
                var publicToken = OptionalString(item, "public_token");
                if (string.IsNullOrEmpty(publicToken))
                    continue;
                string? institutionName = null;
                if (item.TryGetProperty("institution", out var institution))
                    institutionName = OptionalString(institution, "name");
                return new PlaidLinkResult(publicToken, institutionName);
            }
        }

        return null;
    }

    public async Task<PlaidLinkResult?> PollLinkResultAsync(
        string linkToken, int maxAttempts, BackoffScheduler backoff,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken cancellationToken = default)
    {
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "At least one attempt is required.");

        var wait = delay ?? ((d, ct) => Task.Delay(d, ct));
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await GetLinkResultAsync(linkToken, cancellationToken);
            if (result is not null)
                return result;
            // No token yet — wait out the propagation lag before the next read, but never after the last.
            if (attempt < maxAttempts - 1)
                await wait(backoff.NextDelay(attempt), cancellationToken);
        }
        return null;
    }

    public async Task<PlaidItemAccess> ExchangePublicTokenAsync(string publicToken, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var payload = new { client_id = opts.ClientId, secret = opts.Secret, public_token = publicToken };

        using var document = await PostAsync("/item/public_token/exchange", payload, cancellationToken);
        var root = document.RootElement;
        return new PlaidItemAccess(
            root.GetProperty("access_token").GetString() ?? string.Empty,
            root.GetProperty("item_id").GetString() ?? string.Empty,
            OptionalString(root, "institution_name"));
    }

    public async Task<IReadOnlyList<PlaidAccount>> GetBalancesAsync(string accessToken, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var payload = new { client_id = opts.ClientId, secret = opts.Secret, access_token = accessToken };

        using var document = await PostAsync("/accounts/balance/get", payload, cancellationToken);
        var accounts = new List<PlaidAccount>();
        foreach (var account in document.RootElement.GetProperty("accounts").EnumerateArray())
        {
            var balances = account.TryGetProperty("balances", out var b) ? b : default;
            accounts.Add(new PlaidAccount(
                account.GetProperty("account_id").GetString() ?? string.Empty,
                OptionalString(account, "name") ?? string.Empty,
                OptionalString(account, "official_name"),
                OptionalString(account, "mask"),
                OptionalString(account, "type") ?? string.Empty,
                OptionalString(account, "subtype"),
                OptionalString(balances, "iso_currency_code"),
                OptionalDouble(balances, "current"),
                OptionalDouble(balances, "available"),
                OptionalDouble(balances, "limit")));
        }
        return accounts;
    }

    public async Task<IReadOnlyList<PlaidLiability>> GetLiabilitiesAsync(string accessToken, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var payload = new { client_id = opts.ClientId, secret = opts.Secret, access_token = accessToken };

        using var document = await PostAsync("/liabilities/get", payload, cancellationToken);
        var result = new List<PlaidLiability>();
        if (!document.RootElement.TryGetProperty("liabilities", out var liabilities))
            return result;

        if (liabilities.TryGetProperty("credit", out var credit) && credit.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in credit.EnumerateArray())
            {
                result.Add(new PlaidLiability(
                    OptionalString(item, "account_id") ?? string.Empty,
                    OptionalDouble(item, "last_statement_balance"),
                    OptionalDate(item, "next_payment_due_date"),
                    OptionalDouble(item, "minimum_payment_amount"),
                    PurchaseApr(item)));
            }
        }

        if (liabilities.TryGetProperty("mortgage", out var mortgage) && mortgage.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in mortgage.EnumerateArray())
            {
                double? rate = null;
                if (item.TryGetProperty("interest_rate", out var interest))
                    rate = OptionalDouble(interest, "percentage");
                result.Add(new PlaidLiability(
                    OptionalString(item, "account_id") ?? string.Empty,
                    null,
                    OptionalDate(item, "next_payment_due_date"),
                    OptionalDouble(item, "next_monthly_payment"),
                    rate));
            }
        }

        if (liabilities.TryGetProperty("student", out var student) && student.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in student.EnumerateArray())
            {
                result.Add(new PlaidLiability(
                    OptionalString(item, "account_id") ?? string.Empty,
                    OptionalDouble(item, "last_statement_balance"),
                    OptionalDate(item, "next_payment_due_date"),
                    OptionalDouble(item, "minimum_payment_amount"),
                    OptionalDouble(item, "interest_rate_percentage")));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<PlaidTransaction>> GetTransactionsAsync(
        string accessToken, IReadOnlyList<string> accountIds,
        DateOnly startDate, DateOnly endDate, int count, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var payload = new
        {
            client_id = opts.ClientId,
            secret = opts.Secret,
            access_token = accessToken,
            start_date = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            end_date = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            options = new { count, offset = 0, account_ids = accountIds }
        };

        using var document = await PostAsync("/transactions/get", payload, cancellationToken);
        var result = new List<PlaidTransaction>();
        if (!document.RootElement.TryGetProperty("transactions", out var transactions) ||
            transactions.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var transaction in transactions.EnumerateArray())
        {
            result.Add(new PlaidTransaction(
                OptionalString(transaction, "transaction_id") ?? string.Empty,
                OptionalString(transaction, "account_id") ?? string.Empty,
                OptionalDate(transaction, "date") ?? default,
                OptionalString(transaction, "name") ?? string.Empty,
                OptionalString(transaction, "merchant_name"),
                OptionalDouble(transaction, "amount") ?? 0,
                OptionalString(transaction, "iso_currency_code"),
                transaction.TryGetProperty("pending", out var pending) && pending.ValueKind == JsonValueKind.True));
        }
        return result;
    }

    public async Task RemoveItemAsync(string accessToken, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var payload = new { client_id = opts.ClientId, secret = opts.Secret, access_token = accessToken };
        using var _ = await PostAsync("/item/remove", payload, cancellationToken);
    }

    private async Task<JsonDocument> PostAsync<T>(string path, T payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.BaseUrl + path)
        {
            Content = JsonContent.Create(payload)
        };

        var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ParseError(body);
            // Only a rejection of the client_id/secret flags the Plaid credentials; an item/token problem
            // or a throttle is not the shared credentials' fault and is left for the per-institution path.
            if (PlaidFailure.IsCredentialRejection(code))
                healthSink?.MarkFailing(CredentialIds.Plaid, code ?? ((int)response.StatusCode).ToString());
            throw ToException(response.StatusCode, code, message);
        }

        // A successful call means the client_id/secret authenticated — clear any prior credentials failure.
        healthSink?.MarkOk(CredentialIds.Plaid);
        return JsonDocument.Parse(body);
    }

    // Plaid returns HTTP 429 with error_code RATE_LIMIT_EXCEEDED when throttled; classify it as a
    // transient rate-limit so the worker reschedules. Everything else is a descriptive hard failure
    // that preserves Plaid's error_code/error_message body for diagnosis.
    private static Exception ToException(HttpStatusCode status, string? code, string? message)
    {
        if (status == HttpStatusCode.TooManyRequests || code == "RATE_LIMIT_EXCEEDED")
            return new RateLimitException($"Plaid rate limited: {(int)status} ({code}): {message}");

        return new HttpRequestException(
            $"Plaid request failed: {(int)status} ({code}): {message}", inner: null, status);
    }

    private static (string? Code, string? Message) ParseError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return (OptionalString(root, "error_code"), OptionalString(root, "error_message"));
        }
        catch (JsonException)
        {
            return (null, body);
        }
    }

    // The purchase APR is the rate a balance checker wants to show; fall back to the first reported APR.
    private static double? PurchaseApr(JsonElement credit)
    {
        if (!credit.TryGetProperty("aprs", out var aprs) || aprs.ValueKind != JsonValueKind.Array)
            return null;

        double? first = null;
        foreach (var apr in aprs.EnumerateArray())
        {
            var value = OptionalDouble(apr, "apr_percentage");
            first ??= value;
            if (OptionalString(apr, "apr_type") == "purchase_apr")
                return value;
        }
        return first;
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? OptionalDouble(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    // Plaid dates are "YYYY-MM-DD"; treat them as a calendar date at UTC midnight.
    private static DateTimeOffset? OptionalDate(JsonElement element, string name)
    {
        var text = OptionalString(element, name);
        if (text is null)
            return null;
        return DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }
}
