using System.Net;
using System.Text;
using System.Text.Json;
using Storyworthtelling.Remarkable.Tests.Support;

namespace Storyworthtelling.Remarkable.Tests;

public class RemarkableStorageClientTests
{
    private const string UploadUrl = "https://internal.cloud.remarkable.com/doc/v2/files";

    private static (RemarkableStorageClient Client, RecordingHandler Handler) Create(params HttpResponseMessage[] responses)
    {
        var handler = new RecordingHandler(responses);
        var client = new RemarkableStorageClient(new HttpClient(handler), new StubAuthProvider());
        return (client, handler);
    }

    private static JsonElement DecodeMeta(HttpRequestMessage request)
    {
        var meta = request.Headers.GetValues("rm-meta").Single();
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(meta));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task Upload_PostsToRoot_WithTitle_AndPdfBody()
    {
        var (client, handler) = Create(RecordingHandler.Ok());

        await client.Upload("NYT Crossword 2026-04-30", [37, 80, 68, 70]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(UploadUrl, request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("fake-user-token", request.Headers.Authorization.Parameter);

        var meta = DecodeMeta(request);
        Assert.Equal("NYT Crossword 2026-04-30", meta.GetProperty("file_name").GetString());
        // Root delivery — no parent, byte-identical to the original behavior.
        Assert.False(meta.TryGetProperty("parent", out _));
    }

    [Fact]
    public async Task Upload_IntoAFolder_SetsParentInRmMeta()
    {
        var (client, handler) = Create(RecordingHandler.Ok());

        await client.Upload("Book — Draft 2 — Analysis", [1, 2, 3], parentFolderId: "folder-123");

        var meta = DecodeMeta(handler.Requests.Single());
        Assert.Equal("folder-123", meta.GetProperty("parent").GetString());
    }

    [Fact]
    public async Task Upload_Throws_OnNonSuccess()
    {
        var (client, _) = Create(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.Upload("Test", [1, 2, 3]));
    }

    [Fact]
    public async Task Upload_Throws_IncludingStatusAndResponseBody()
    {
        // The reMarkable cloud explains a refusal in the body; the exception must carry it so a device-side
        // failure (D-04) is diagnosable instead of a bare status code.
        var response = new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
        {
            Content = new StringContent("{\"message\":\"method not allowed on this route\"}"),
        };
        var (client, _) = Create(response);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.Upload("Test", [1, 2, 3]));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, ex.StatusCode);
        Assert.Contains("405", ex.Message);
        Assert.Contains("method not allowed on this route", ex.Message);
        Assert.Contains("Test", ex.Message);
    }

    [Fact]
    public async Task EnsureFolder_ReturnsExistingCollection_WithoutCreating()
    {
        // The listing already contains a "Canonizer" collection — reuse it, no second (create) request.
        var listing = RecordingHandler.Ok(
            "[{\"id\":\"col-1\",\"file_name\":\"Canonizer\",\"type\":\"CollectionType\"}," +
            "{\"id\":\"doc-1\",\"file_name\":\"Canonizer\",\"type\":\"DocumentType\"}]");
        var (client, handler) = Create(listing);

        var id = await client.EnsureFolder("Canonizer");

        Assert.Equal("col-1", id);                       // the collection, not the same-named document
        Assert.Single(handler.Requests);                 // only the GET listing, no create POST
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task EnsureFolder_CreatesCollection_WhenAbsent()
    {
        // First response: an empty listing. Second: the create response carrying the new id.
        var (client, handler) = Create(RecordingHandler.Ok("[]"), RecordingHandler.Ok("{\"id\":\"new-col\"}"));

        var id = await client.EnsureFolder("Canonizer");

        Assert.Equal("new-col", id);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        var meta = DecodeMeta(handler.Requests[1]);
        Assert.Equal("Canonizer", meta.GetProperty("file_name").GetString());
        Assert.Equal("collection", meta.GetProperty("type").GetString());
    }
}
