using Google.Apis.Auth.OAuth2;
using Google.Apis.Docs.v1;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Folio.Domain.External;
using Folio.Domain.Text;

namespace Folio.Infrastructure.External;

// Fetches a picked document's text using the short-lived OAuth access token the in-app Drive picker
// grants. A native Google Doc is read through the Docs API; every other supported format — .docx, RTF,
// ePub, PDF — is downloaded from Drive as raw bytes and its text extracted locally, because the
// Docs API only serves Google's own document type. The format is discovered from the file's Drive mime
// type (falling back to its name's extension), so callers don't have to know or pass it. The manuscript
// text is read once at ingest and then worked from the database.
public sealed class GoogleDocsClient : IGoogleDocsClient
{
    private const string ApplicationName = "Folio";
    private const string GoogleDocMime = "application/vnd.google-apps.document";

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

        var (mimeType, name) = await ResolveFile(initializer, documentId, cancellationToken);

        // A native Google Doc goes through the Docs API; anything else is a byte download + local extract.
        if (string.Equals(mimeType, GoogleDocMime, StringComparison.OrdinalIgnoreCase))
            return await FetchGoogleDocText(initializer, documentId, cancellationToken);

        var extract = ExtractorFor(mimeType, name);
        if (extract is null)
            // An unrecognized type: fall back to the Google Doc path, which still works for a native Doc
            // whose mime we couldn't read.
            return await FetchGoogleDocText(initializer, documentId, cancellationToken);

        var bytes = await DownloadBytes(initializer, documentId, cancellationToken);
        return extract(bytes);
    }

    // Picks the local extractor for a downloaded file by its Drive mime type, falling back to the file
    // name's extension (some sources report a generic octet-stream). Null when the format isn't one we
    // extract locally.
    private static Func<byte[], string>? ExtractorFor(string? mimeType, string? name)
    {
        var mime = mimeType?.ToLowerInvariant() ?? string.Empty;
        var ext = Path.GetExtension(name ?? string.Empty).ToLowerInvariant();

        if (mime.Contains("wordprocessingml.document") || ext == ".docx")
            return DocxTextExtractor.Extract;
        if (mime is "application/rtf" or "text/rtf" || ext == ".rtf")
            return bytes => RtfTextExtractor.Extract(System.Text.Encoding.UTF8.GetString(bytes));
        if (mime == "application/epub+zip" || ext == ".epub")
            return EpubTextExtractor.Extract;
        if (mime == "application/pdf" || ext == ".pdf")
            return PdfTextExtractor.Extract;
        return null;
    }

    private static async Task<(string? MimeType, string? Name)> ResolveFile(
        BaseClientService.Initializer initializer, string fileId, CancellationToken cancellationToken)
    {
        using var drive = new DriveService(initializer);
        var request = drive.Files.Get(fileId);
        request.Fields = "mimeType,name";
        var file = await request.ExecuteAsync(cancellationToken);
        return (file.MimeType, file.Name);
    }

    private static async Task<string> FetchGoogleDocText(
        BaseClientService.Initializer initializer, string documentId, CancellationToken cancellationToken)
    {
        using var service = new DocsService(initializer);
        var document = await service.Documents.Get(documentId).ExecuteAsync(cancellationToken);
        return DocumentTextExtractor.Extract(document);
    }

    private static async Task<byte[]> DownloadBytes(
        BaseClientService.Initializer initializer, string fileId, CancellationToken cancellationToken)
    {
        using var drive = new DriveService(initializer);
        using var buffer = new MemoryStream();
        await drive.Files.Get(fileId).DownloadAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
