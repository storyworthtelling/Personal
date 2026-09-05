using System.Net;
using Microsoft.Extensions.Options;
using DailyLoader.Infrastructure.Google;
using Xunit;

namespace DailyLoader.Tests.Google;

public class GoogleAccessTokenProviderTests
{
    private static GoogleAccessTokenProvider Create(StubHandler handler, GoogleOptions options) =>
        new(new HttpClient(handler), Options.Create(options));

    private static StubHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(json) });

    [Fact]
    public async Task GetAccessToken_ReturnsTokenFromRefresh()
    {
        var provider = Create(
            Json("""{"access_token":"abc123","expires_in":3600}"""),
            new GoogleOptions { ClientId = "id", ClientSecret = "secret", RefreshToken = "rt" });

        Assert.Equal("abc123", await provider.GetAccessToken());
    }

    [Fact]
    public async Task GetAccessToken_CachesUntilExpiry()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"tok","expires_in":3600}""")
            };
        });
        var provider = Create(handler, new GoogleOptions { ClientId = "id", RefreshToken = "rt" });

        await provider.GetAccessToken();
        await provider.GetAccessToken();

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetAccessToken_OmitsSecretWhenAbsent()
    {
        string? capturedBody = null;
        var handler = new StubHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"tok","expires_in":3600}""")
            };
        });
        var provider = Create(handler, new GoogleOptions { ClientId = "native-id", RefreshToken = "rt" });

        await provider.GetAccessToken();

        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("client_secret", capturedBody);
        Assert.Contains("refresh_token", capturedBody);
    }

    [Fact]
    public async Task GetAccessToken_ThrowsWhenNoRefreshToken()
    {
        var provider = Create(Json("{}"), new GoogleOptions { ClientId = "id" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAccessToken());
    }

    [Fact]
    public async Task GetAccessToken_ThrowsOnHttpError()
    {
        var provider = Create(
            Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest),
            new GoogleOptions { ClientId = "id", RefreshToken = "bad" });

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetAccessToken());
    }

    [Fact]
    public async Task ExchangeCodeForRefreshToken_ReturnsRefreshToken()
    {
        var handler = Json("""{"access_token":"a","refresh_token":"new-rt","expires_in":3600}""");

        var rt = await GoogleAccessTokenProvider.ExchangeCodeForRefreshToken(
            new HttpClient(handler), "id", null, "code", "verifier", "redirect");

        Assert.Equal("new-rt", rt);
    }

    [Fact]
    public async Task ExchangeCodeForRefreshToken_ThrowsWhenNoRefreshTokenReturned()
    {
        var handler = Json("""{"access_token":"a","expires_in":3600}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GoogleAccessTokenProvider.ExchangeCodeForRefreshToken(
                new HttpClient(handler), "id", null, "code", "verifier", "redirect"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> sync)
            : this(req => Task.FromResult(sync(req))) { }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
