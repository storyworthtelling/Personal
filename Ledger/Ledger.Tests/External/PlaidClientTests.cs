using System.Net;
using Microsoft.Extensions.Options;
using Index.Domain.Pipeline;
using Index.Infrastructure.Configuration;
using Index.Infrastructure.External;
using Index.Tests.Support;

namespace Index.Tests.External;

public class PlaidClientTests
{
    private static (PlaidClient Client, FakeHttpMessageHandler Handler) Create(string environment = "sandbox")
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new PlaidOptions
        {
            ClientId = "test-client",
            Secret = "test-secret",
            Environment = environment
        });
        return (new PlaidClient(new HttpClient(handler), options), handler);
    }

    [Fact]
    public async Task GetBalances_ParsesAccountsAndBalances()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("""
        {
          "accounts": [
            {
              "account_id": "acc1",
              "balances": { "available": 100.5, "current": 110.0, "limit": null, "iso_currency_code": "USD" },
              "mask": "1234",
              "name": "Plaid Checking",
              "official_name": "Plaid Gold Checking",
              "type": "depository",
              "subtype": "checking"
            }
          ],
          "request_id": "req1"
        }
        """);

        var accounts = await client.GetBalancesAsync("access-token", CancellationToken.None);

        var account = Assert.Single(accounts);
        Assert.Equal("acc1", account.AccountId);
        Assert.Equal("Plaid Checking", account.Name);
        Assert.Equal("Plaid Gold Checking", account.OfficialName);
        Assert.Equal("1234", account.Mask);
        Assert.Equal("depository", account.Type);
        Assert.Equal("checking", account.Subtype);
        Assert.Equal("USD", account.IsoCurrencyCode);
        Assert.Equal(110.0m, account.Current);
        Assert.Equal(100.5m, account.Available);
        Assert.Null(account.Limit);
    }

    [Fact]
    public async Task GetBalances_PostsCredentialsAndAccessTokenToEnvironmentHost()
    {
        var (client, handler) = Create("production");
        handler.EnqueueOk("""{ "accounts": [], "request_id": "r" }""");

        await client.GetBalancesAsync("access-42", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://production.plaid.com/accounts/balance/get", request.RequestUri!.ToString());
        var body = handler.RequestBodies[0];
        Assert.Contains("\"client_id\":\"test-client\"", body);
        Assert.Contains("\"secret\":\"test-secret\"", body);
        Assert.Contains("\"access_token\":\"access-42\"", body);
    }

    [Fact]
    public async Task GetLiabilities_ParsesCreditAndMortgage()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("""
        {
          "liabilities": {
            "credit": [
              {
                "account_id": "card1",
                "aprs": [
                  { "apr_percentage": 12.5, "apr_type": "balance_transfer_apr" },
                  { "apr_percentage": 19.99, "apr_type": "purchase_apr" }
                ],
                "last_statement_balance": 500.25,
                "minimum_payment_amount": 25.0,
                "next_payment_due_date": "2026-08-15"
              }
            ],
            "mortgage": [
              {
                "account_id": "mort1",
                "interest_rate": { "percentage": 3.5, "type": "fixed" },
                "next_monthly_payment": 1200.0,
                "next_payment_due_date": "2026-08-01"
              }
            ],
            "student": []
          },
          "request_id": "req2"
        }
        """);

        var liabilities = await client.GetLiabilitiesAsync("access-token", CancellationToken.None);

        Assert.Equal(2, liabilities.Count);

        var card = liabilities.Single(l => l.AccountId == "card1");
        Assert.Equal(500.25m, card.LastStatementBalance);
        Assert.Equal(25.0m, card.NextPaymentAmount);
        Assert.Equal(19.99m, card.Apr); // the purchase APR is preferred over the balance-transfer APR
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero), card.NextPaymentDueDate);

        var mortgage = liabilities.Single(l => l.AccountId == "mort1");
        Assert.Equal(3.5m, mortgage.Apr);
        Assert.Equal(1200.0m, mortgage.NextPaymentAmount);
        Assert.Null(mortgage.LastStatementBalance);
    }

    [Fact]
    public async Task GetTransactions_ParsesRecentTransactions_AndFiltersByAccount()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("""
        {
          "transactions": [
            {
              "transaction_id": "txn1",
              "account_id": "acc1",
              "date": "2026-07-15",
              "name": "SQ *BLUE BOTTLE",
              "merchant_name": "Blue Bottle Coffee",
              "amount": 6.75,
              "iso_currency_code": "USD",
              "pending": false
            },
            {
              "transaction_id": "txn2",
              "account_id": "acc1",
              "date": "2026-07-14",
              "name": "AUTOPAY THANK YOU",
              "amount": -120.00,
              "iso_currency_code": "USD",
              "pending": true
            }
          ],
          "total_transactions": 2,
          "request_id": "r"
        }
        """);

        var start = new DateOnly(2026, 7, 1);
        var end = new DateOnly(2026, 7, 19);
        var transactions = await client.GetTransactionsAsync(
            "access-token", new[] { "acc1" }, start, end, 15, CancellationToken.None);

        Assert.Equal(2, transactions.Count);
        var first = transactions[0];
        Assert.Equal("txn1", first.TransactionId);
        Assert.Equal("acc1", first.AccountId);
        Assert.Equal("Blue Bottle Coffee", first.MerchantName);
        Assert.Equal(6.75, first.Amount);
        Assert.False(first.Pending);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero), first.Date);
        Assert.True(transactions[1].Pending);
        Assert.Equal(-120.00, transactions[1].Amount);

        var body = handler.RequestBodies[0];
        Assert.Contains("\"start_date\":\"2026-07-01\"", body);
        Assert.Contains("\"end_date\":\"2026-07-19\"", body);
        Assert.Contains("\"account_ids\":[\"acc1\"]", body);
    }

    [Fact]
    public async Task GetTransactions_AsksPlaidForThePersonalFinanceCategory_AndReadsItAndThePendingPointer()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """
        {
          "transactions": [
            {
              "transaction_id": "posted-1",
              "pending_transaction_id": "pending-1",
              "account_id": "acc1",
              "date": "2026-07-15",
              "name": "WHOLEFDS",
              "merchant_name": "Whole Foods",
              "amount": 42.50,
              "iso_currency_code": "USD",
              "pending": false,
              "personal_finance_category": {
                "primary": "FOOD_AND_DRINK",
                "detailed": "FOOD_AND_DRINK_GROCERIES",
                "confidence_level": "VERY_HIGH"
              }
            },
            {
              "transaction_id": "bare-1",
              "pending_transaction_id": null,
              "account_id": "acc1",
              "date": "2026-07-14",
              "name": "SOMETHING",
              "amount": 3.00,
              "pending": true,
              "personal_finance_category": null
            }
          ],
          "total_transactions": 2,
          "request_id": "r"
        }
        """);

        var transactions = await client.GetTransactionsAsync(
            "access-token", new[] { "acc1" }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 19), 250, CancellationToken.None);

        // Plaid only returns personal_finance_category when the request opts in (R8).
        Assert.Contains("\"include_personal_finance_category\":true", handler.RequestBodies[0]);
        Assert.Contains("\"account_ids\":[\"acc1\"]", handler.RequestBodies[0]);

        var posted = transactions[0];
        Assert.Equal("pending-1", posted.PendingTransactionId);
        Assert.Equal("FOOD_AND_DRINK", posted.CategoryPrimary);
        Assert.Equal("FOOD_AND_DRINK_GROCERIES", posted.CategoryDetailed);

        var bare = transactions[1];
        Assert.Null(bare.PendingTransactionId);
        Assert.Null(bare.CategoryPrimary);
        Assert.Null(bare.CategoryDetailed);
    }

    [Fact]
    public async Task RateLimited429_ThrowsRateLimitException()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.TooManyRequests, """
        { "error_type": "RATE_LIMIT_EXCEEDED", "error_code": "RATE_LIMIT_EXCEEDED",
          "error_message": "too many requests", "request_id": "r" }
        """);

        await Assert.ThrowsAsync<RateLimitException>(
            () => client.GetBalancesAsync("access-token", CancellationToken.None));
    }

    [Fact]
    public async Task NonRateLimitError_ThrowsHttpRequestExceptionWithPlaidCode()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.BadRequest, """
        { "error_type": "INVALID_INPUT", "error_code": "INVALID_ACCESS_TOKEN",
          "error_message": "the access token is invalid", "request_id": "r" }
        """);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetBalancesAsync("bad-token", CancellationToken.None));
        Assert.Contains("INVALID_ACCESS_TOKEN", ex.Message);
    }

    [Fact]
    public async Task ExchangePublicToken_ParsesAccessTokenAndItemId()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("""
        { "access_token": "access-sandbox-xyz", "item_id": "item-123", "request_id": "r" }
        """);

        var access = await client.ExchangePublicTokenAsync("public-token", CancellationToken.None);

        Assert.Equal("access-sandbox-xyz", access.AccessToken);
        Assert.Equal("item-123", access.ItemId);
        Assert.Contains("\"public_token\":\"public-token\"", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task GetLinkResult_ReturnsPublicTokenAndInstitution_WhenSessionComplete()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("""
        {
          "link_token": "link-sandbox-abc",
          "link_sessions": [
            {
              "results": {
                "item_add_results": [
                  {
                    "public_token": "public-sandbox-99",
                    "institution": { "name": "PNC Bank", "institution_id": "ins_13" }
                  }
                ]
              }
            }
          ]
        }
        """);

        var result = await client.GetLinkResultAsync("link-sandbox-abc", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("public-sandbox-99", result!.PublicToken);
        Assert.Equal("PNC Bank", result.InstitutionName);
    }

    [Fact]
    public async Task GetLinkResult_ReturnsNull_WhenSessionIncomplete()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("""{ "link_token": "link-sandbox-abc", "link_sessions": [] }""");

        var result = await client.GetLinkResultAsync("link-sandbox-abc", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task PollLinkResult_ReturnsToken_AsSoonAsTheSessionBecomesReady()
    {
        var (client, handler) = Create();
        // Plaid populates item_add_results a short, variable time AFTER the OAuth redirect returns
        // (pronounced for app-to-app OAuth banks), so the first reads see an empty session
        // and only a later one carries the public_token.
        handler.EnqueueOk("""{ "link_sessions": [] }""");
        handler.EnqueueOk("""{ "link_sessions": [] }""");
        handler.EnqueueOk("""
        {
          "link_sessions": [
            { "results": { "item_add_results": [
              { "public_token": "public-sandbox-99", "institution": { "name": "Redwood Bank" } } ] } }
          ]
        }
        """);

        var result = await client.PollLinkResultAsync(
            "link-sandbox-abc", maxAttempts: 6,
            new BackoffScheduler(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8)),
            delay: (_, _) => Task.CompletedTask, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("public-sandbox-99", result!.PublicToken);
        Assert.Equal("Redwood Bank", result.InstitutionName);
        Assert.Equal(3, handler.Requests.Count); // stopped polling the instant the token appeared
    }

    [Fact]
    public async Task PollLinkResult_ReturnsNull_WhenSessionNeverCompletesWithinBudget()
    {
        var (client, handler) = Create();
        for (var i = 0; i < 4; i++)
            handler.EnqueueOk("""{ "link_sessions": [] }""");

        var result = await client.PollLinkResultAsync(
            "link-sandbox-abc", maxAttempts: 4,
            new BackoffScheduler(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8)),
            delay: (_, _) => Task.CompletedTask, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(4, handler.Requests.Count); // exactly the attempt budget, never more
    }

    [Fact]
    public async Task CreateLinkToken_ParsesHostedLinkUrl_RequestsTransactionsAndLiabilities()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("""
        { "link_token": "link-sandbox-abc",
          "hosted_link_url": "https://hosted.plaid.com/link?token=abc", "request_id": "r" }
        """);

        var token = await client.CreateLinkTokenAsync(CancellationToken.None);

        Assert.Equal("link-sandbox-abc", token.LinkToken);
        Assert.Equal("https://hosted.plaid.com/link?token=abc", token.HostedLinkUrl);
        var body = handler.RequestBodies[0];
        // Hosted Link is a WebView flow, not the native Android SDK, so android_package_name is not sent —
        // sending it makes Plaid reject the token (INVALID_FIELD) until the package is registered in the
        // dashboard, which a hosted integration never needs.
        Assert.DoesNotContain("android_package_name", body);
        // Plaid rejects an empty products array (INVALID_PRODUCT); transactions is the required product,
        // liabilities is requested only where the institution supports it (can't be in both lists).
        Assert.Contains("\"products\":[\"transactions\"]", body);
        Assert.Contains("\"required_if_supported_products\":[\"liabilities\"]", body);
        Assert.Contains("hosted_link", body);
    }

    [Fact]
    public async Task GetInstitution_ParsesPrimaryColor()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("""
        {
          "institution": {
            "institution_id": "ins_13",
            "name": "PNC Bank",
            "primary_color": "#025a2f"
          },
          "request_id": "r"
        }
        """);

        var institution = await client.GetInstitutionAsync("ins_13", CancellationToken.None);

        Assert.Equal("ins_13", institution.InstitutionId);
        Assert.Equal("PNC Bank", institution.Name);
        Assert.Equal("#025a2f", institution.PrimaryColorHex);

        var body = handler.RequestBodies[0];
        Assert.Contains("\"institution_id\":\"ins_13\"", body);
        Assert.Contains("\"include_optional_metadata\":true", body);
        Assert.Equal(
            "https://sandbox.plaid.com/institutions/get_by_id", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetInstitution_ParsesAbsentColor_AsNull()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("""
        { "institution": { "institution_id": "ins_1", "name": "Small Credit Union" }, "request_id": "r" }
        """);

        var institution = await client.GetInstitutionAsync("ins_1", CancellationToken.None);

        Assert.Null(institution.PrimaryColorHex);
    }

    [Fact]
    public async Task GetItemInstitutionId_ReadsItemInstitutionId_FromTheAccessToken()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("""
        { "item": { "item_id": "item-1", "institution_id": "ins_13" }, "request_id": "r" }
        """);

        var institutionId = await client.GetItemInstitutionIdAsync("access-1", CancellationToken.None);

        Assert.Equal("ins_13", institutionId);
        Assert.Contains("\"access_token\":\"access-1\"", handler.RequestBodies[0]);
        Assert.Equal("https://sandbox.plaid.com/item/get", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetItemInstitutionId_NullInstitutionId_ReadsAsNull()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("""
        { "item": { "item_id": "item-1", "institution_id": null }, "request_id": "r" }
        """);

        Assert.Null(await client.GetItemInstitutionIdAsync("access-1", CancellationToken.None));
    }
}
