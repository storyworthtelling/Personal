using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

public class TidalAuthClientTests
{
    private const string DeviceAuthUrl = "https://auth.tidal.com/v1/oauth2/device_authorization";
    private const string TokenUrl = "https://auth.tidal.com/v1/oauth2/token";

    private static (TidalAuthClient Client, FakeHttpMessageHandler Handler) Build()
    {
        var handler = new FakeHttpMessageHandler();
        return (new TidalAuthClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task ExchangeCodeAsync_SendsTheCodeTheVerifierAndTheRedirect()
    {
        var (client, handler) = Build();
        handler.Respond(TokenUrl, """
            {"access_token":"acc","refresh_token":"ref","token_type":"Bearer","expires_in":604800,
             "user":{"userId":12345,"countryCode":"US"}}
            """);

        var result = await client.ExchangeCodeAsync(
            "cid", "secret", "the-code", "the-verifier", "app://back", CancellationToken.None);

        Assert.Equal(TidalTokenPollStatus.Success, result.Status);
        Assert.Equal("ref", result.RefreshToken);
        Assert.Equal("12345", result.UserId);

        var body = handler.RequestBodies.Single();
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("code=the-code", body);
        Assert.Contains("code_verifier=the-verifier", body);
        Assert.Contains("redirect_uri=app%3A%2F%2Fback", body);
    }

    [Fact]
    public async Task ExchangeCodeAsync_AuthenticatesTheClient()
    {
        var (client, handler) = Build();
        handler.Respond(TokenUrl, """{"access_token":"a","refresh_token":"r","expires_in":1}""");

        await client.ExchangeCodeAsync("cid", "sec", "c", "v", "app://back", CancellationToken.None);

        var auth = handler.Requests.Single().Headers.Authorization;
        Assert.Equal("Basic", auth!.Scheme);
        Assert.Equal(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("cid:sec")),
            auth.Parameter);
    }

    // A refused exchange must not read as a signed-in account with no tokens in it.
    [Fact]
    public async Task ExchangeCodeAsync_OnARefusal_IsNotASuccess()
    {
        var (client, handler) = Build();
        handler.Respond(TokenUrl, """{"error":"invalid_grant"}""", status: System.Net.HttpStatusCode.BadRequest);

        var result = await client.ExchangeCodeAsync(
            "cid", "sec", "c", "v", "app://back", CancellationToken.None);

        Assert.NotEqual(TidalTokenPollStatus.Success, result.Status);
        Assert.Null(result.RefreshToken);
    }

    // The five RFC 8628 codes above are the polling loop's own vocabulary. The browser/PKCE
    // exchange endpoint can also answer with TIDAL's own REST error shape — a numeric subStatus
    // and a userMessage, no "error" field at all — which used to fall through to a bare Error
    // status with nothing kept. This is the exact shape behind the owner's unexplained "1002 Error".
    [Fact]
    public async Task ExchangeCodeAsync_OnTidalsOwnRestErrorShape_CarriesTheSubStatusAndUserMessage()
    {
        var (client, handler) = Build();
        handler.Respond(
            TokenUrl, """{"status":400,"subStatus":1002,"userMessage":"Invalid code_verifier"}""",
            status: System.Net.HttpStatusCode.BadRequest);

        var result = await client.ExchangeCodeAsync(
            "cid", "sec", "c", "v", "app://back", CancellationToken.None);

        Assert.Equal(TidalTokenPollStatus.Error, result.Status);
        Assert.NotNull(result.Detail);
        Assert.Contains("1002", result.Detail);
        Assert.Contains("Invalid code_verifier", result.Detail);
    }

    [Fact]
    public async Task ExchangeCodeAsync_OnASuccess_HasNoDetail()
    {
        var (client, handler) = Build();
        handler.Respond(TokenUrl, """{"access_token":"a","refresh_token":"r","expires_in":1,"user":{"userId":1}}""");

        var result = await client.ExchangeCodeAsync(
            "cid", "sec", "c", "v", "app://back", CancellationToken.None);

        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task RefreshAsync_ExchangesTheStoredRefreshTokenForANewAccessToken()
    {
        var (client, handler) = Build();
        handler.Respond(TokenUrl, """{"access_token":"fresh","refresh_token":"ref2","expires_in":604800,"user":{"userId":9}}""");

        var result = await client.RefreshAsync("client-id", "client-secret", "old-refresh", CancellationToken.None);

        Assert.Equal(TidalTokenPollStatus.Success, result.Status);
        Assert.Equal("fresh", result.AccessToken);
        Assert.Equal(
            "client_id=client-id&grant_type=refresh_token&refresh_token=old-refresh",
            handler.RequestBodies.Single());
    }
}
