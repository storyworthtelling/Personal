using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NytRemarkable.Infrastructure.Remarkable;
using Xunit;

namespace NytRemarkable.Tests.Remarkable;

public class RemarkableStorageClientTests
{
    private const string FakeUserToken = "header.eyJzdWIiOiJhdXRoMHx1c2VyMSIsImV4cCI6OTk5OTk5OTk5OX0.sig";
    private const string FakeStorageHost = "document-storage-production-dot-remarkable-production.appspot.com";

    private static (RemarkableStorageClient client, List<(string url, HttpMethod method, byte[]? body)> log)
        CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var log = new List<(string, HttpMethod, byte[]?)>();
        var httpHandler = new RoutingHandler(handler, log);
        var httpClient = new HttpClient(httpHandler);

        var authService = new FakeAuthService(FakeUserToken);
        var client = new RemarkableStorageClient(httpClient, authService);
        return (client, log);
    }

    private static HttpResponseMessage ServiceDiscoveryResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { Host = FakeStorageHost }),
                Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage UploadRequestResponse(string documentId) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new[]
                {
                    new { ID = documentId, BlobURLPut = "https://storage.googleapis.com/blob/upload", Success = true, Message = "" }
                }),
                Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task Upload_CallsServiceDiscovery()
    {
        var calls = new List<string>();
        var (client, _) = CreateClient(req =>
        {
            calls.Add(req.RequestUri!.ToString());
            if (req.RequestUri.Host == "service-manager-production-dot-remarkable-production.appspot.com")
                return ServiceDiscoveryResponse();
            if (req.RequestUri.AbsolutePath.Contains("upload/request"))
                return UploadRequestResponse("test-id");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        });

        await client.Upload("Test Puzzle", [1, 2, 3]);

        Assert.Contains(calls, c => c.Contains("service-manager-production-dot-remarkable-production.appspot.com"));
    }

    [Fact]
    public async Task Upload_UsesDiscoveredHostForDocumentOperations()
    {
        var documentId = Guid.NewGuid().ToString();
        var uploadRequestCalls = new List<string>();

        var (client, _) = CreateClient(req =>
        {
            if (req.RequestUri!.Host == "service-manager-production-dot-remarkable-production.appspot.com")
                return ServiceDiscoveryResponse();
            if (req.RequestUri.AbsolutePath.Contains("upload/request"))
            {
                uploadRequestCalls.Add(req.RequestUri.Host);
                return UploadRequestResponse(documentId);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        });

        await client.Upload("Test Puzzle", [1, 2, 3]);

        Assert.Contains(FakeStorageHost, uploadRequestCalls);
    }

    [Fact]
    public async Task Upload_UploadsZipContainingPdfAndContentFile()
    {
        byte[]? capturedZip = null;

        var (client, _) = CreateClient(req =>
        {
            if (req.RequestUri!.Host == "service-manager-production-dot-remarkable-production.appspot.com")
                return ServiceDiscoveryResponse();
            if (req.RequestUri.AbsolutePath.Contains("upload/request"))
                return UploadRequestResponse("doc-123");
            if (req.RequestUri.Host == "storage.googleapis.com")
            {
                capturedZip = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        });

        var pdfBytes = new byte[] { 37, 80, 68, 70 }; // %PDF
        await client.Upload("Test Puzzle", pdfBytes);

        Assert.NotNull(capturedZip);
        using var archive = new ZipArchive(new MemoryStream(capturedZip), ZipArchiveMode.Read);
        var entries = archive.Entries.Select(e => e.Name).ToList();
        Assert.Contains(entries, e => e.EndsWith(".pdf"));
        Assert.Contains(entries, e => e.EndsWith(".content"));
    }

    [Fact]
    public async Task Upload_UpdatesDocumentStatusWithCorrectTitle()
    {
        string? capturedTitle = null;

        var (client, _) = CreateClient(req =>
        {
            if (req.RequestUri!.Host == "service-manager-production-dot-remarkable-production.appspot.com")
                return ServiceDiscoveryResponse();
            if (req.RequestUri.AbsolutePath.Contains("upload/request"))
                return UploadRequestResponse("doc-abc");
            if (req.RequestUri.Host == "storage.googleapis.com")
                return new HttpResponseMessage(HttpStatusCode.OK);
            if (req.RequestUri.AbsolutePath.Contains("update-status"))
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(body);
                capturedTitle = doc.RootElement[0].GetProperty("VissibleName").GetString();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        });

        await client.Upload("NYT Crossword 2025-01-05", [1, 2, 3]);

        Assert.Equal("NYT Crossword 2025-01-05", capturedTitle);
    }

    private sealed class RoutingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        List<(string url, HttpMethod method, byte[]? body)> log) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            log.Add((request.RequestUri!.ToString(), request.Method, null));
            return Task.FromResult(handler(request));
        }
    }

    private sealed class FakeAuthService(string token) : IRemarkableAuthProvider
    {
        public Task<string> GetUserToken() => Task.FromResult(token);
    }
}
