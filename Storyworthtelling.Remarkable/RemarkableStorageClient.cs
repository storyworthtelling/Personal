using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Storyworthtelling.Remarkable;

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

    public Task Upload(string title, byte[] pdfBytes) => Upload(title, pdfBytes, null);

    public async Task Upload(string title, byte[] pdfBytes, string? parentFolderId)
    {
        // The document metadata: file_name always, and parent only when delivering into a folder — an
        // empty/absent parent means the account root, so the root path is byte-identical to before.
        var meta = string.IsNullOrEmpty(parentFolderId)
            ? Base64Meta(new { file_name = title })
            : Base64Meta(new { file_name = title, parent = parentFolderId });

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

    public async Task<string> EnsureFolder(string name)
    {
        // Reuse an existing folder of this name (so delivery doesn't spawn duplicate folders); otherwise
        // create one. The collection wire format is reconstructed from the docs — a metadata-only POST with
        // type=collection and no PDF body — and the first live run verifies the cloud accepts it.
        var existing = await FindCollectionId(name);
        if (existing is not null)
            return existing;

        var meta = Base64Meta(new { file_name = name, type = "collection" });
        using var request = await CreateRequest(HttpMethod.Post, FilesUrl);
        request.Headers.Add("rm-meta", meta);
        request.Headers.Add("rm-source", "RoR-Browser");

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"reMarkable folder create failed: {(int)response.StatusCode} ({response.StatusCode}) for '{name}'",
                inner: null,
                response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        return ReadCreatedId(body)
            ?? throw new HttpRequestException($"reMarkable folder create for '{name}' returned no id.");
    }

    public async Task<IReadOnlyList<RemarkableDoc>> ListRecent()
    {
        // NOTE: the v2 read path is not exercised by the upload flow and needs verification against the
        // live reMarkable cloud. The metadata listing is parsed tolerantly so a schema difference surfaces
        // as an empty/partial list rather than a crash.
        using var request = await CreateRequest(HttpMethod.Get, FilesUrl);
        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"reMarkable list failed: {(int)response.StatusCode} ({response.StatusCode})",
                inner: null,
                response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        return ParseEntries(json).Select(e => new RemarkableDoc { Id = e.Id, Title = e.Title, PdfBytes = [] }).ToList();
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

    private static string Base64Meta(object metadata) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata)));

    private async Task<HttpRequestMessage> CreateRequest(HttpMethod method, string url)
    {
        var userToken = await authProvider.GetUserToken();
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        return request;
    }

    // The id of an existing folder (collection) with this name, or null if none exists.
    private async Task<string?> FindCollectionId(string name)
    {
        using var request = await CreateRequest(HttpMethod.Get, FilesUrl);
        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"reMarkable list failed: {(int)response.StatusCode} ({response.StatusCode})",
                inner: null,
                response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        return ParseEntries(json)
            .FirstOrDefault(e => e.IsCollection && string.Equals(e.Title, name, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private sealed record Entry(string Id, string Title, bool IsCollection);

    // Tolerant parse: accepts either a bare array or an object wrapping an array, reads id/title from the
    // common reMarkable field names, and flags collections (folders) by their type field.
    private static List<Entry> ParseEntries(string json)
    {
        var entries = new List<Entry>();
        if (string.IsNullOrWhiteSpace(json))
            return entries;

        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("entries", out var e) ? e
            : root.TryGetProperty("documents", out var d) ? d
            : default;

        if (array.ValueKind != JsonValueKind.Array)
            return entries;

        foreach (var item in array.EnumerateArray())
        {
            var id = ReadString(item, "id", "ID", "DocumentID");
            var title = ReadString(item, "file_name", "VissibleName", "visibleName", "name");
            var type = ReadString(item, "type", "Type");
            if (id is not null && title is not null)
                entries.Add(new Entry(id, title, IsCollectionType(type)));
        }

        return entries;
    }

    private static bool IsCollectionType(string? type) =>
        type is not null && type.Contains("collection", StringComparison.OrdinalIgnoreCase);

    private static string? ReadCreatedId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        using var parsed = JsonDocument.Parse(json);
        return ReadString(parsed.RootElement, "id", "ID", "DocumentID");
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }
}
