using System.Net;
using Microsoft.Extensions.Options;
using Ledger.Domain.Pipeline;
using Ledger.Infrastructure.Configuration;
using Ledger.Infrastructure.External;
using Ledger.Tests.Support;

namespace Ledger.Tests.External;

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
        Assert.Equal(110.0, account.Current);
        Assert.Equal(100.5, account.Available);
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
        Assert.Equal(500.25, card.LastStatementBalance);
        Assert.Equal(25.0, card.NextPaymentAmount);
        Assert.Equal(19.99, card.Apr); // the purchase APR is preferred over the balance-transfer APR
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero), card.NextPaymentDueDate);

        var mortgage = liabilities.Single(l => l.AccountId == "mort1");
        Assert.Equal(3.5, mortgage.Apr);
        Assert.Equal(1200.0, mortgage.NextPaymentAmount);
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
    public async Task CreateLinkToken_ParsesHostedLinkUrlAndRequestsLiabilities()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("""
        { "link_token": "link-sandbox-abc",
          "hosted_link_url": "https://hosted.plaid.com/link?token=abc", "request_id": "r" }
        """);

        var token = await client.CreateLinkTokenAsync("com.storyworthtelling.ledger", CancellationToken.None);

        Assert.Equal("link-sandbox-abc", token.LinkToken);
        Assert.Equal("https://hosted.plaid.com/link?token=abc", token.HostedLinkUrl);
        var body = handler.RequestBodies[0];
        Assert.Contains("\"android_package_name\":\"com.storyworthtelling.ledger\"", body);
        Assert.Contains("liabilities", body);
        Assert.Contains("hosted_link", body);
    }
}
