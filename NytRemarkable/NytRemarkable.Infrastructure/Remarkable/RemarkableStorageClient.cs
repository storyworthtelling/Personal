using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NytRemarkable.Domain.Interfaces;

namespace NytRemarkable.Infrastructure.Remarkable;

public class RemarkableStorageClient : IRemarkableClient
{
    private const string ServiceDiscoveryUrl =
        "https://service-manager-production-dot-remarkable-production.appspot.com" +
        "/service/json/1/document-storage?environment=production&group={0}&apiVer=2";

    private readonly HttpClient httpClient;
    private readonly IRemarkableAuthProvider authProvider;

    public RemarkableStorageClient(HttpClient httpClient, IRemarkableAuthProvider authProvider)
    {
        this.httpClient = httpClient;
        this.authProvider = authProvider;
    }

    public async Task Upload(string title, byte[] pdfBytes)
    {
        var userToken = await authProvider.GetUserToken();
        var (userId, _) = JwtPayload.Parse(userToken);

        var storageHost = await DiscoverStorageHost(userToken, userId);
        var documentId = Guid.NewGuid().ToString();
        var blobUrl = await RequestUploadSlot(storageHost, userToken, documentId);

        var zipBytes = CreateDocumentPackage(documentId, pdfBytes);
        await UploadToBlob(blobUrl, zipBytes);

        await UpdateDocumentStatus(storageHost, userToken, documentId, title);
    }

    private async Task<string> DiscoverStorageHost(string userToken, string userId)
    {
        var url = string.Format(ServiceDiscoveryUrl, Uri.EscapeDataString(userId));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"reMarkable service discovery failed: {(int)response.StatusCode} ({response.StatusCode})");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("Host").GetString()
            ?? throw new InvalidOperationException("Service discovery response missing 'Host'");
    }

    private async Task<string> RequestUploadSlot(string host, string userToken, string documentId)
    {
        var url = $"https://{host}/document-storage/json/2/upload/request";
        var body = JsonSerializer.Serialize(new[]
        {
            new { ID = documentId, Parent = string.Empty, Type = "DocumentType", Version = 1 }
        });

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"reMarkable upload request failed: {(int)response.StatusCode} ({response.StatusCode})");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.EnumerateArray().First();

        if (!entry.GetProperty("Success").GetBoolean())
            throw new InvalidOperationException(
                $"reMarkable upload slot rejected: {entry.GetProperty("Message").GetString()}");

        return entry.GetProperty("BlobURLPut").GetString()
            ?? throw new InvalidOperationException("Upload response missing 'BlobURLPut'");
    }

    private async Task UploadToBlob(string blobUrl, byte[] zipBytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, blobUrl);
        request.Content = new ByteArrayContent(zipBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"reMarkable blob upload failed: {(int)response.StatusCode} ({response.StatusCode})");
    }

    private async Task UpdateDocumentStatus(string host, string userToken, string documentId, string title)
    {
        var url = $"https://{host}/document-storage/json/2/upload/update-status";
        var body = JsonSerializer.Serialize(new[]
        {
            new
            {
                ID = documentId,
                Parent = string.Empty,
                Type = "DocumentType",
                Version = 1,
                ModifiedClient = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                VissibleName = title,
                CurrentPage = 0,
                Bookmarked = false,
                Message = string.Empty
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"reMarkable document status update failed: {(int)response.StatusCode} ({response.StatusCode})");
    }

    private static byte[] CreateDocumentPackage(string documentId, byte[] pdfBytes)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var contentEntry = archive.CreateEntry($"{documentId}.content");
            using (var writer = new StreamWriter(contentEntry.Open()))
                writer.Write("""{"fileType":"pdf","extraMetadata":{},"pageCount":0,"textScale":1,"transform":{}}""");

            var pdfEntry = archive.CreateEntry($"{documentId}.pdf");
            using var pdfStream = pdfEntry.Open();
            pdfStream.Write(pdfBytes);
        }
        return stream.ToArray();
    }
}
