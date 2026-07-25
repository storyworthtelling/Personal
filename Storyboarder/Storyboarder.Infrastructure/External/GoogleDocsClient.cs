using Google.Apis.Auth.OAuth2;
using Google.Apis.Docs.v1;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Text;

namespace Storyboarder.Infrastructure.External;

// Fetches a picked document's text using the short-lived OAuth access token the in-app Drive picker
// grants. Two source kinds are supported (the owner asked for .docx alongside Google Docs, nothing
// else): a native Google Doc is read through the Docs API; a .docx is downloaded from Drive as raw
// bytes and its text extracted locally, because the Docs API only serves Google's own document type.
// The kind is discovered from the file's Drive mime type, so callers don't have to know or pass it. The
// novel text is read once at ingest and then worked from the database.
public sealed class GoogleDocsClient : IGoogleDocsClient
{
    private const string ApplicationName = "Storyboarder";
    private const string GoogleDocMime = "application/vnd.google-apps.document";
    private const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public async Task<string> FetchText(string documentId, string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document id is required.", nameof(documentId));
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("Access token is required.", nameof(accessToken));

        var credential = GoogleCredential.FromAccessToken(accessToken);
        var initializer = new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName
        };

        var mimeType = await ResolveMimeType(initializer, documentId, cancellationToken);

        // A .docx is downloaded and extracted locally; anything else is treated as a Google Doc (the
        // long-standing path), so a native Doc — or a file whose type we couldn't read — still works.
        if (string.Equals(mimeType, DocxMime, StringComparison.OrdinalIgnoreCase))
            return await FetchDocxText(initializer, documentId, cancellationToken);

        return await FetchGoogleDocText(initializer, documentId, cancellationToken);
    }

    private static async Task<string?> ResolveMimeType(
        BaseClientService.Initializer initializer, string fileId, CancellationToken cancellationToken)
    {
        using var drive = new DriveService(initializer);
        var request = drive.Files.Get(fileId);
        request.Fields = "mimeType";
        var file = await request.ExecuteAsync(cancellationToken);
        return file.MimeType;
    }

    private static async Task<string> FetchGoogleDocText(
        BaseClientService.Initializer initializer, string documentId, CancellationToken cancellationToken)
    {
        using var service = new DocsService(initializer);
        var document = await service.Documents.Get(documentId).ExecuteAsync(cancellationToken);
        return DocumentTextExtractor.Extract(document);
    }

    private static async Task<string> FetchDocxText(
        BaseClientService.Initializer initializer, string fileId, CancellationToken cancellationToken)
    {
        using var drive = new DriveService(initializer);
        using var buffer = new MemoryStream();
        await drive.Files.Get(fileId).DownloadAsync(buffer, cancellationToken);
        return DocxTextExtractor.Extract(buffer.ToArray());
    }
}
