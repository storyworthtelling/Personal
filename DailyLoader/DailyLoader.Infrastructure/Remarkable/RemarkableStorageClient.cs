using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DailyLoader.Domain.Interfaces;
using DailyLoader.Domain.Models;

namespace DailyLoader.Infrastructure.Remarkable;

public class RemarkableStorageClient : IRemarkableClient
{
    private const string FilesUrl = "https://internal.cloud.remarkable.com/doc/v2/files";

    private readonly HttpClient httpClient;
    private readonly IRemarkableAuthProvider authProvider;

    public RemarkableStorageClient(HttpClient httpClient, IRemarkableAuthProvider authProvider)
    {
        this.httpClient = httpClient;
        this.authProvider = authProvider;
    }

    public async Task Upload(string title, byte[] pdfBytes)
    {
        var meta = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { file_name = title })));

        using var request = await CreateRequest(HttpMethod.Post, FilesUrl);
        request.Headers.Add("rm-meta", meta);
        request.Headers.Add("rm-source", "RoR-Browser");
        request.Content = new ByteArrayContent(pdfBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"reMarkable upload failed: {(int)response.StatusCode} ({response.StatusCode}) for '{title}'",
                inner: null,
                response.StatusCode);
    }

    public async Task<IReadOnlyList<RemarkableDoc>> ListRecent()
    {
        // NOTE: the v2 read path is not exercised by the upload flow and needs verification
        // against the live reMarkable cloud (see the read-back spike in the plan). The metadata
        // listing is parsed tolerantly so a schema difference surfaces as an empty/partial list
        // rather than a crash.
        using var request = await CreateRequest(HttpMethod.Get, FilesUrl);
        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"reMarkable list failed: {(int)response.StatusCode} ({response.StatusCode})",
                inner: null,
                response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        return ParseDocList(json);
    }

    public async Task<RemarkableDoc?> DownloadByTitle(string title)
    {
        var match = (await ListRecent()).FirstOrDefault(d =>
            string.Equals(d.Title, title, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return null;

        using var request = await CreateRequest(HttpMethod.Get, $"{FilesUrl}/{match.Id}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));
        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"reMarkable download failed: {(int)response.StatusCode} ({response.StatusCode}) for '{title}'",
                inner: null,
                response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        return new RemarkableDoc { Id = match.Id, Title = match.Title, PdfBytes = bytes };
    }

    public async Task MoveToTrash(string docId)
    {
        using var request = await CreateRequest(HttpMethod.Delete, $"{FilesUrl}/{docId}");
        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"reMarkable trash failed: {(int)response.StatusCode} ({response.StatusCode}) for '{docId}'",
                inner: null,
                response.StatusCode);
    }

    private async Task<HttpRequestMessage> CreateRequest(HttpMethod method, string url)
    {
        var userToken = await authProvider.GetUserToken();
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        return request;
    }

    // Tolerant parse: accepts either a bare array or an object wrapping an array, and reads
    // id/title from the common reMarkable field names without assuming one exact schema.
    private static List<RemarkableDoc> ParseDocList(string json)
    {
        var docs = new List<RemarkableDoc>();
        if (string.IsNullOrWhiteSpace(json))
            return docs;

        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("entries", out var entries) ? entries
            : root.TryGetProperty("documents", out var documents) ? documents
            : default;

        if (array.ValueKind != JsonValueKind.Array)
            return docs;

        foreach (var item in array.EnumerateArray())
        {
            var id = ReadString(item, "id", "ID", "DocumentID");
            var title = ReadString(item, "file_name", "VissibleName", "visibleName", "name");
            if (id is not null && title is not null)
                docs.Add(new RemarkableDoc { Id = id, Title = title, PdfBytes = [] });
        }

        return docs;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }
}
