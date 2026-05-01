using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using NytRemarkable.Infrastructure.Remarkable;
using Xunit;

namespace NytRemarkable.Tests.Remarkable;

public class RemarkableAuthServiceTests
{
    private static string CreateFakeJwt(string sub, DateTimeOffset expiry)
    {
        var header = ToBase64Url("""{"alg":"RS256","typ":"JWT"}""");
        var payload = ToBase64Url($$$"""{"sub":"{{{sub}}}","exp":{{{expiry.ToUnixTimeSeconds()}}}}""");
        return $"{header}.{payload}.fakesignature";
    }

    private static string ToBase64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static RemarkableAuthService CreateService(
        HttpResponseMessage response,
        out List<HttpRequestMessage> capturedRequests,
        string deviceToken = "device-token-value")
    {
        var requests = new List<HttpRequestMessage>();
        capturedRequests = requests;

        var handler = new CapturingHandler(response, requests);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new RemarkableOptions { DeviceToken = deviceToken });
        return new RemarkableAuthService(httpClient, options);
    }

    [Fact]
    public async Task GetUserToken_PostsToCorrectEndpoint()
    {
        var token = CreateFakeJwt("auth0|user1", DateTimeOffset.UtcNow.AddHours(24));
        var service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"\"{token}\"") },
            out var requests);

        await service.GetUserToken();

        Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.Equal(
            "https://webapp-prod.cloud.remarkable.engineering/token/json/2/user/new",
            requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetUserToken_SendsDeviceTokenInAuthorizationHeader()
    {
        var token = CreateFakeJwt("auth0|user1", DateTimeOffset.UtcNow.AddHours(24));
        var service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"\"{token}\"") },
            out var requests,
            deviceToken: "my-device-token");

        await service.GetUserToken();

        Assert.Equal("Bearer", requests[0].Headers.Authorization!.Scheme);
        Assert.Equal("my-device-token", requests[0].Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task GetUserToken_ReturnsTokenFromResponse()
    {
        var expectedToken = CreateFakeJwt("auth0|user1", DateTimeOffset.UtcNow.AddHours(24));
        var service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"\"{expectedToken}\"") },
            out _);

        var result = await service.GetUserToken();

        Assert.Equal(expectedToken, result);
    }

    [Fact]
    public async Task GetUserToken_CachesToken_WhenCalledTwice()
    {
        var token = CreateFakeJwt("auth0|user1", DateTimeOffset.UtcNow.AddHours(24));
        var service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"\"{token}\"") },
            out var requests);

        await service.GetUserToken();
        await service.GetUserToken();

        Assert.Single(requests);
    }

    [Fact]
    public async Task GetUserToken_Throws_WhenServerReturnsError()
    {
        var service = CreateService(
            new HttpResponseMessage(HttpStatusCode.Unauthorized),
            out _);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetUserToken());
    }

    private sealed class CapturingHandler(HttpResponseMessage response, List<HttpRequestMessage> captured)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            captured.Add(request);
            return Task.FromResult(response);
        }
    }
}
