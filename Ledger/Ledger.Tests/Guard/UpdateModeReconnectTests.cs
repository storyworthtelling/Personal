using Microsoft.Extensions.Options;
using Index.Domain.Connect;
using Index.Domain.External;
using Index.Domain.Pipeline;
using Index.Infrastructure.Configuration;
using Index.Infrastructure.External;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guard G16 (D150): reconnecting a broken bank must open Plaid Link's UPDATE MODE against the
/// existing Item's access token, never mint a fresh Link — the Trial plan's ten-Item cap counts Items
/// CREATED, so a "repair" that quietly created a new Item would spend one of the household's ten Trial
/// connections on a bank that was never actually lost.
///
/// <para><c>PlaidLinkPage</c> (the Waiting page's Reconnect action's actual destination) lives in
/// <c>Ledger.Android</c>, a <c>net10.0-android</c> project this desktop-TFM test project cannot load —
/// so this proves the same thing at the boundary it CAN reach: <see cref="ReconnectFlow"/> is the one
/// call that page's update-mode branch makes, and the first test below proves the real
/// <see cref="PlaidClient"/> actually carries the access token onto the wire when asked for an update
/// token, the same idiom <c>PlaidClientTests</c> already uses (fake HTTP, no network).</para>
/// </summary>
public class UpdateModeReconnectTests
{
    [Fact]
    public async Task CreateUpdateLinkToken_PostsTheExistingAccessTokenToLinkTokenCreate()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new PlaidOptions { ClientId = "test-client", Secret = "test-secret", Environment = "production" });
        var client = new PlaidClient(new HttpClient(handler), options);
        handler.EnqueueOk("""
        { "link_token": "link-update-1", "hosted_link_url": "https://hosted.plaid.com/link?token=upd" }
        """);

        var token = await client.CreateUpdateLinkTokenAsync("existing-access-42", CancellationToken.None);

        Assert.Equal("link-update-1", token.LinkToken);
        Assert.Equal("https://hosted.plaid.com/link?token=upd", token.HostedLinkUrl);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://production.plaid.com/link/token/create", request.RequestUri!.ToString());
        var body = handler.RequestBodies[0];
        Assert.Contains("\"access_token\":\"existing-access-42\"", body);
        // A reintroduced bug this proves itself against: the fresh-Link overload's products/
        // required_if_supported_products pair leaking into the update-mode request body — Plaid infers
        // an existing Item's products from the Item itself, and a second declaration is not part of the
        // documented update-mode shape.
        Assert.DoesNotContain("required_if_supported_products", body);
    }

    // The reintroduced bug this half proves itself against: PlaidLinkPage's update-mode branch calling
    // the plain CreateLinkTokenAsync() (fresh-Link) overload instead of routing through ReconnectFlow —
    // watched red first by pointing this assertion at a spy that recorded a call to the FRESH overload,
    // then fixed to assert against the (correct) update-mode call recorded below.
    [Fact]
    public async Task ReconnectFlow_CallsTheUpdateModeOverload_NeverTheFreshLinkOverload()
    {
        var spy = new RecordingPlaidClient();

        var token = await ReconnectFlow.BeginAsync(spy, "existing-access-77", CancellationToken.None);

        Assert.True(spy.UpdateModeCalled);
        Assert.Equal("existing-access-77", spy.CapturedAccessToken);
        Assert.False(spy.FreshLinkCalled);
        Assert.Equal("update-link", token.LinkToken);
    }

    private sealed class RecordingPlaidClient : IPlaidClient
    {
        public bool FreshLinkCalled;
        public bool UpdateModeCalled;
        public string? CapturedAccessToken;

        public Task<PlaidLinkToken> CreateLinkTokenAsync(CancellationToken cancellationToken)
        {
            FreshLinkCalled = true;
            return Task.FromResult(new PlaidLinkToken("fresh-link", "https://hosted.plaid.com/link?token=fresh"));
        }

        public Task<PlaidLinkToken> CreateUpdateLinkTokenAsync(string existingAccessToken, CancellationToken cancellationToken)
        {
            UpdateModeCalled = true;
            CapturedAccessToken = existingAccessToken;
            return Task.FromResult(new PlaidLinkToken("update-link", "https://hosted.plaid.com/link?token=update"));
        }

        public Task<PlaidLinkResult?> GetLinkResultAsync(string linkToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PlaidLinkResult?> PollLinkResultAsync(
            string linkToken, int maxAttempts, BackoffScheduler backoff,
            Func<TimeSpan, CancellationToken, Task>? delay = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PlaidItemAccess> ExchangePublicTokenAsync(string publicToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlaidAccount>> GetBalancesAsync(string accessToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlaidLiability>> GetLiabilitiesAsync(string accessToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlaidTransaction>> GetTransactionsAsync(
            string accessToken, IReadOnlyList<string> accountIds,
            DateOnly startDate, DateOnly endDate, int count, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveItemAsync(string accessToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PlaidInstitution> GetInstitutionAsync(string institutionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> GetItemInstitutionIdAsync(string accessToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
