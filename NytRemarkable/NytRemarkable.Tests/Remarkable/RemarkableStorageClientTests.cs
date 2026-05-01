using System.Net;
using System.Text;
using System.Text.Json;
using NytRemarkable.Infrastructure.Remarkable;
using Xunit;

namespace NytRemarkable.Tests.Remarkable;

public class RemarkableStorageClientTests
{
    private const string FakeUserToken = "fake-user-token";
    private const string UploadUrl = "https://internal.cloud.remarkable.com/doc/v2/files";

    private static (RemarkableStorageClient client, List<HttpRequestMessage> requests)
        CreateClient(HttpResponseMessage response)
    {
        var requests = new List<HttpRequestMessage>();
        var httpClient = new HttpClient(new CapturingHandler(response, requests));
        var client = new RemarkableStorageClient(httpClient, new FakeAuthService(FakeUserToken));
        return (client, requests);
    }

    [Fact]
    public async Task Upload_PostsToCorrectUrl()
    {
        var (client, requests) = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        });

        await client.Upload("Test Puzzle", [1, 2, 3]);

        Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.Equal(UploadUrl, requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Upload_SetsTitleInRmMetaHeader()
    {
        var (client, requests) = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        });

        await client.Upload("NYT Crossword 2026-04-30", [1, 2, 3]);

        Assert.True(requests[0].Headers.TryGetValues("rm-meta", out var values));
        var meta = values!.Single();
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(meta));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("NYT Crossword 2026-04-30", doc.RootElement.GetProperty("file_name").GetString());
    }

    [Fact]
    public async Task Upload_SendsRawPdfBytesAsBody()
    {
        byte[]? capturedBody = null;
        var handler = new DelegatingCapturingHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsByteArrayAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var httpClient = new HttpClient(handler);
        var client = new RemarkableStorageClient(httpClient, new FakeAuthService(FakeUserToken));

        var pdfBytes = new byte[] { 37, 80, 68, 70 }; // %PDF
        await client.Upload("Test", pdfBytes);

        Assert.Equal(pdfBytes, capturedBody);
    }

    [Fact]
    public async Task Upload_IncludesAuthorizationHeader()
    {
        var (client, requests) = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        });

        await client.Upload("Test", [1]);

        Assert.Equal("Bearer", requests[0].Headers.Authorization!.Scheme);
        Assert.Equal(FakeUserToken, requests[0].Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Upload_Throws_WhenResponseIsNotSuccess()
    {
        var (client, _) = CreateClient(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.Upload("Test", [1, 2, 3]));
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

    private sealed class DelegatingCapturingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }

    private sealed class FakeAuthService(string token) : IRemarkableAuthProvider
    {
        public Task<string> GetUserToken() => Task.FromResult(token);
    }
}
