using System.Net;
using System.Text;
using Folio.Infrastructure.External;
using Google.Apis.Http;

namespace Folio.Tests.External;

// Error-handling audit §1: Drive's downloader reports a 401/403/429/5xx or a dropped connection in the
// progress it returns rather than throwing. GoogleDocsClient ignored it, so a failed download of an RTF
// read as an empty manuscript and a .docx as a corrupt one. A failed download must say it failed.
public class GoogleDocsClientDownloadTests
{
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AFailedDownloadThrowsRatherThanReadingAsAnEmptyManuscript(HttpStatusCode status)
    {
        var client = new GoogleDocsClient(new Factory(new Drive(status)));

        var thrown = await Assert.ThrowsAsync<IOException>(() => client.FetchText("doc-1", "token", CancellationToken.None));

        Assert.NotNull(thrown.InnerException);
    }

    [Fact]
    public async Task ADownloadThatWorksIsExtracted()
    {
        var client = new GoogleDocsClient(new Factory(new Drive(null)));

        var text = await client.FetchText("doc-1", "token", CancellationToken.None);

        Assert.Contains("Chapter one", text, StringComparison.Ordinal);
    }

    private sealed class Drive(HttpStatusCode? mediaFailure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var media = request.RequestUri!.Query.Contains("alt=media", StringComparison.Ordinal);
            if (!media)
            {
                return Task.FromResult(Json("{\"mimeType\":\"application/rtf\",\"name\":\"draft.rtf\"}"));
            }
            if (mediaFailure is { } failure)
            {
                return Task.FromResult(new HttpResponseMessage(failure)
                {
                    Content = new StringContent($"{{\"error\":{{\"code\":{(int)failure},\"message\":\"injected\"}}}}", Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{\rtf1\ansi Chapter one.\par}", Encoding.UTF8, "application/rtf"),
            });
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class Factory(HttpMessageHandler handler) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
    }
}
