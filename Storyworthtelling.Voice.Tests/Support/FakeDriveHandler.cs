using System.Net;
using System.Text;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;

namespace Storyworthtelling.Voice.Tests.Support;

// A tiny in-memory Drive behind the real DriveService: files.list, files.get (metadata and alt=media),
// files.update (trash) and the resumable files.create upload. It answers from what it holds, so a test
// drives DriveLogFolder exactly as the phone would and asserts on what Drive ended up with.
public sealed class FakeDriveHandler : HttpMessageHandler
{
    public sealed record FakeFile(string Id, string Name, string? Parent, string MimeType, bool OwnedByMe)
    {
        public string Content { get; set; } = string.Empty;
        public bool Trashed { get; set; }
    }

    private int nextId = 1;
    private readonly Dictionary<string, (string Name, string? Parent, string MimeType)> pendingUploads = [];

    public List<FakeFile> Files { get; } = [];
    public List<string> Requests { get; } = [];

    public FakeFile Add(string name, string? parent, string content = "", bool ownedByMe = true,
        string mimeType = "application/json")
    {
        var file = new FakeFile($"id{nextId++}", name, parent, mimeType, ownedByMe) { Content = content };
        Files.Add(file);
        return file;
    }

    public FakeFile AddFolder(string name) => Add(name, null, mimeType: "application/vnd.google-apps.folder");

    public DriveService Service() => new(new BaseClientService.Initializer
    {
        ApplicationName = "tests",
        HttpClientFactory = new Factory(this),
        GZipEnabled = false,
    });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add($"{request.Method} {url.AbsolutePath}{url.Query}");
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        var path = url.AbsolutePath;

        if (path.StartsWith("/upload/drive/v3/files", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
        {
            var metadata = System.Text.Json.JsonDocument.Parse(body).RootElement;
            var parent = metadata.TryGetProperty("parents", out var parents) ? parents[0].GetString() : null;
            var session = $"session{pendingUploads.Count + 1}";
            pendingUploads[session] = (metadata.GetProperty("name").GetString()!, parent, "application/json");
            var started = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
            started.Headers.Location = new Uri($"https://www.googleapis.com/upload/drive/v3/files?upload_id={session}");
            return started;
        }
        if (path.StartsWith("/upload/drive/v3/files", StringComparison.Ordinal) && request.Method == HttpMethod.Put)
        {
            var (name, parent, mime) = pendingUploads[query["upload_id"]!];
            var created = Add(name, parent, body, mimeType: mime);
            return Json($"{{\"id\":\"{created.Id}\"}}");
        }
        if (path == "/drive/v3/files" && request.Method == HttpMethod.Get)
        {
            return Json(List(query["q"] ?? string.Empty));
        }
        if (path == "/drive/v3/files" && request.Method == HttpMethod.Post)
        {
            var metadata = System.Text.Json.JsonDocument.Parse(body).RootElement;
            var folder = Add(metadata.GetProperty("name").GetString()!, null,
                mimeType: metadata.GetProperty("mimeType").GetString()!);
            return Json($"{{\"id\":\"{folder.Id}\"}}");
        }
        if (path.StartsWith("/drive/v3/files/", StringComparison.Ordinal))
        {
            var id = path["/drive/v3/files/".Length..];
            var file = Files.FirstOrDefault(f => f.Id == id);
            if (file is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"error\":{\"code\":404,\"message\":\"File not found\"}}", Encoding.UTF8, "application/json"),
                };
            }
            if (request.Method == HttpMethod.Patch)
            {
                if (body.Contains("\"trashed\":true", StringComparison.Ordinal))
                {
                    file.Trashed = true;
                }
                return Json($"{{\"id\":\"{file.Id}\"}}");
            }
            if (query["alt"] == "media")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(file.Content, Encoding.UTF8, "application/json") };
            }
            return Json(Describe(file));
        }
        throw new InvalidOperationException($"The fake Drive has no answer for {request.Method} {url}");
    }

    // Understands the handful of clauses DriveLogFolder writes: name='…', mimeType='…', '<id>' in parents,
    // trashed=false. Anything else in a query fails the test rather than being ignored.
    private string List(string q)
    {
        IEnumerable<FakeFile> matches = Files;
        foreach (var clause in q.Split(" and ", StringSplitOptions.TrimEntries))
        {
            if (clause.StartsWith("name='", StringComparison.Ordinal))
            {
                var name = clause["name='".Length..^1].Replace(@"\'", "'", StringComparison.Ordinal);
                matches = matches.Where(f => f.Name == name);
            }
            else if (clause.StartsWith("mimeType='", StringComparison.Ordinal))
            {
                var mime = clause["mimeType='".Length..^1];
                matches = matches.Where(f => f.MimeType == mime);
            }
            else if (clause.EndsWith("' in parents", StringComparison.Ordinal))
            {
                var parent = clause[1..^"' in parents".Length];
                matches = matches.Where(f => f.Parent == parent);
            }
            else if (clause == "trashed=false")
            {
                matches = matches.Where(f => !f.Trashed);
            }
            else
            {
                throw new InvalidOperationException($"The fake Drive does not understand '{clause}'.");
            }
        }
        return "{\"files\":[" + string.Join(",", matches.Select(Describe)) + "]}";
    }

    private static string Describe(FakeFile file) =>
        $"{{\"id\":\"{file.Id}\",\"name\":\"{file.Name}\",\"ownedByMe\":{(file.OwnedByMe ? "true" : "false")},\"trashed\":{(file.Trashed ? "true" : "false")},\"modifiedTime\":\"2026-10-31T12:00:00Z\"}}";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Factory(FakeDriveHandler handler) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
    }
}
