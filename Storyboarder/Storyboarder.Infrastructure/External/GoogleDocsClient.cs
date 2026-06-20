using Google.Apis.Auth.OAuth2;
using Google.Apis.Docs.v1;
using Google.Apis.Services;
using Storyboarder.Domain.External;

namespace Storyboarder.Infrastructure.External;

// Fetches a Google Doc's text using the short-lived OAuth access token the in-app Drive picker
// grants in the browser. The novel text is read once at ingest and then worked from the database.
public sealed class GoogleDocsClient : IGoogleDocsClient
{
    private const string ApplicationName = "Storyboarder";

    public async Task<string> FetchText(string documentId, string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document id is required.", nameof(documentId));
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("Access token is required.", nameof(accessToken));

        var credential = GoogleCredential.FromAccessToken(accessToken);
        using var service = new DocsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName
        });

        var document = await service.Documents.Get(documentId).ExecuteAsync(cancellationToken);
        return DocumentTextExtractor.Extract(document);
    }
}
