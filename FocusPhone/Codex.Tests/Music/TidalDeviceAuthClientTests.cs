using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

public class TidalDeviceAuthClientTests
{
    private const string DeviceAuthUrl = "https://auth.tidal.com/v1/oauth2/device_authorization";
    private const string TokenUrl = "https://auth.tidal.com/v1/oauth2/token";

    private static (TidalDeviceAuthClient Client, FakeHttpMessageHandler Handler) Build()
    {
        var handler = new FakeHttpMessageHandler();
        return (new TidalDeviceAuthClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task RequestDeviceCodeAsync_ReadsEveryFieldTheOwnerScreenNeeds()
    {
        var (client, handler) = Build();
        handler.Respond(DeviceAuthUrl, """
            {"deviceCode":"d-123","userCode":"ABCD-EFGH",
             "verificationUri":"link.tidal.com","verificationUriComplete":"link.tidal.com?ABCD-EFGH",
             "expiresIn":300,"interval":2}
            """);

        var auth = await client.RequestDeviceCodeAsync("client-id", "secret", CancellationToken.None);

        Assert.Equal("d-123", auth.DeviceCode);
        Assert.Equal("ABCD-EFGH", auth.UserCode);
        Assert.Equal("link.tidal.com", auth.VerificationUri);
        Assert.Equal("link.tidal.com?ABCD-EFGH", auth.VerificationUriComplete);
        Assert.Equal(300, auth.ExpiresIn);
        Assert.Equal(2, auth.Interval);
    }

    [Fact]
    public async Task RequestDeviceCodeAsync_SendsTheClientIdAndScopeAsForm()
    {
        var (client, handler) = Build();
        handler.Respond(DeviceAuthUrl,
            """{"deviceCode":"d","userCode":"u","verificationUri":"v","verificationUriComplete":"vc","expiresIn":1,"interval":1}""");

        await client.RequestDeviceCodeAsync("client-id", "secret", CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("client_id=client-id&scope=r_usr+w_usr+w_sub", handler.RequestBodies.Single());
    }

    // RFC 8628 section 3.1: a confidential client MUST authenticate at the device
    // authorization endpoint too, the same way it does at the token endpoint. Score
    // holds a client secret, so it is confidential — and it was sending none here,
    // which is what TIDAL answered 400 to on device.
    [Fact]
    public async Task RequestDeviceCodeAsync_AuthenticatesTheClientLikeTheTokenEndpointDoes()
    {
        var (client, handler) = Build();
        handler.Respond(DeviceAuthUrl,
            """{"deviceCode":"d","userCode":"u","verificationUri":"v","verificationUriComplete":"vc","expiresIn":1,"interval":1}""");

        await client.RequestDeviceCodeAsync("client-id", "the-secret", CancellationToken.None);

        var auth = handler.Requests.Single().Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Basic", auth.Scheme);
        Assert.Equal(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("client-id:the-secret")),
            auth.Parameter);
    }

    // A public client — no secret — must NOT send an empty Basic header, which is a
    // different and equally wrong request.
    [Fact]
    public async Task RequestDeviceCodeAsync_WithNoSecret_SendsNoAuthorizationHeader()
    {
        var (client, handler) = Build();
        handler.Respond(DeviceAuthUrl,
            """{"deviceCode":"d","userCode":"u","verificationUri":"v","verificationUriComplete":"vc","expiresIn":1,"interval":1}""");

        await client.RequestDeviceCodeAsync("client-id", clientSecret: "", CancellationToken.None);

        Assert.Null(handler.Requests.Single().Headers.Authorization);
    }

    // The 400 that started this said only "400". TIDAL puts the reason in the body,
    // and EnsureSuccessStatusCode threw it away — the same mistake as logging to a
    // console nobody can read, one layer down.
    [Fact]
    public async Task RequestDeviceCodeAsync_OnAnErrorReply_CarriesTidalsOwnExplanation()
    {
        var (client, handler) = Build();
        handler.Respond(
            DeviceAuthUrl,
            """{"error":"invalid_scope","error_description":"w_sub is not permitted for this client"}""",
            status: System.Net.HttpStatusCode.BadRequest);

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.RequestDeviceCodeAsync("client-id", "secret", CancellationToken.None));

        Assert.Contains("400", thrown.Message);
        Assert.Contains("invalid_scope", thrown.Message);
        Assert.Contains("w_sub is not permitted for this client", thrown.Message);
    }

    [Fact]
    public async Task PollAsync_OnSuccessReadsTheTokensAndTheAccountId()
    {
        var (client, handler) = Build();
        handler.Respond(TokenUrl, """
            {"access_token":"acc","refresh_token":"ref","token_type":"Bearer","expires_in":604800,
             "user":{"userId":12345,"countryCode":"US"}}
            """);

        var result = await client.PollAsync("id", "secret", "device-code", CancellationToken.None);

        Assert.Equal(TidalTokenPollStatus.Success, result.Status);
        Assert.Equal("acc", result.AccessToken);
        Assert.Equal("ref", result.RefreshToken);
        Assert.Equal(604800, result.ExpiresIn);
        Assert.Equal("12345", result.UserId);
    }

    [Fact]
    public async Task PollAsync_SendsBasicAuthAndTheDeviceCodeGrant()
    {
        var (client, handler) = Build();
        handler.Respond(TokenUrl, """{"access_token":"a","refresh_token":"r","expires_in":1,"user":{"userId":1}}""");

        await client.PollAsync("client-id", "client-secret", "device-code", CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("client-id:client-secret")),
            request.Headers.Authorization.Parameter);
        Assert.Equal(
            "client_id=client-id&device_code=device-code" +
            "&grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code&scope=r_usr+w_usr+w_sub",
            handler.RequestBodies.Single());
    }

    // The ordinary case while the owner has not opened the link yet: RFC 8628 says
    // this is a 400 with a named error, not a transport failure.
    [Theory]
    [InlineData("authorization_pending", TidalTokenPollStatus.Pending)]
    [InlineData("slow_down", TidalTokenPollStatus.SlowDown)]
    [InlineData("access_denied", TidalTokenPollStatus.Denied)]
    [InlineData("expired_token", TidalTokenPollStatus.Expired)]
    public async Task PollAsync_MapsEveryPendingShapeToItsStatus(string errorCode, TidalTokenPollStatus expected)
    {
        var (client, handler) = Build();
        handler.Respond(
            TokenUrl, $$"""{"status":400,"error":"{{errorCode}}","error_description":"nope"}""",
            status: System.Net.HttpStatusCode.BadRequest);

        var result = await client.PollAsync("id", "secret", "device-code", CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.AccessToken);
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

    [Fact]
    public async Task PollUntilAuthorizedAsync_StopsAsSoonAsTheOwnerApproves()
    {
        var (client, handler) = Build();
        handler.RespondSequence(TokenUrl,
            ("""{"status":400,"error":"authorization_pending"}""", System.Net.HttpStatusCode.BadRequest),
            ("""{"status":400,"error":"authorization_pending"}""", System.Net.HttpStatusCode.BadRequest),
            ("""{"access_token":"a","refresh_token":"r","expires_in":1,"user":{"userId":1}}""", System.Net.HttpStatusCode.OK));

        var waits = new List<TimeSpan>();
        var result = await client.PollUntilAuthorizedAsync(
            "id", "secret", "device-code", intervalSeconds: 2, expiresInSeconds: 300,
            delay: (span, _) => { waits.Add(span); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal(TidalTokenPollStatus.Success, result.Status);
        Assert.Equal("a", result.AccessToken);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task PollUntilAuthorizedAsync_WidensTheIntervalOnSlowDown()
    {
        var (client, handler) = Build();
        handler.RespondSequence(TokenUrl,
            ("""{"status":400,"error":"slow_down"}""", System.Net.HttpStatusCode.BadRequest),
            ("""{"access_token":"a","refresh_token":"r","expires_in":1,"user":{"userId":1}}""", System.Net.HttpStatusCode.OK));

        var waits = new List<TimeSpan>();
        await client.PollUntilAuthorizedAsync(
            "id", "secret", "device-code", intervalSeconds: 2, expiresInSeconds: 300,
            delay: (span, _) => { waits.Add(span); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(7)], waits);
    }

    // Denied is a terminal answer, not a transient one — polling again would just ask
    // an owner who already said no.
    [Fact]
    public async Task PollUntilAuthorizedAsync_StopsImmediatelyOnDenied()
    {
        var (client, handler) = Build();
        handler.Respond(TokenUrl, """{"status":400,"error":"access_denied"}""", status: System.Net.HttpStatusCode.BadRequest);

        var result = await client.PollUntilAuthorizedAsync(
            "id", "secret", "device-code", intervalSeconds: 2, expiresInSeconds: 300,
            delay: (_, _) => throw new InvalidOperationException("should not wait after a denial"),
            CancellationToken.None);

        Assert.Equal(TidalTokenPollStatus.Denied, result.Status);
        Assert.Single(handler.Requests);
    }

    // The code's own window ran out. Reported as Expired even though nothing ever
    // answered "expired_token" — the pairing screen needs a fresh code either way.
    [Fact]
    public async Task PollUntilAuthorizedAsync_StopsWhenTheCodesOwnWindowRunsOut()
    {
        var (client, handler) = Build();
        handler.Respond(TokenUrl, """{"status":400,"error":"authorization_pending"}""", status: System.Net.HttpStatusCode.BadRequest);

        var result = await client.PollUntilAuthorizedAsync(
            "id", "secret", "device-code", intervalSeconds: 100, expiresInSeconds: 150,
            delay: (_, _) => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(TidalTokenPollStatus.Expired, result.Status);
        // One immediate poll, one after the 100s wait, then the 150s deadline is reached.
        Assert.Equal(2, handler.Requests.Count);
    }
}
