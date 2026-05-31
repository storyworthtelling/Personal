using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DailyLoader.Domain.Interfaces;

namespace DailyLoader.Infrastructure.Remarkable;

public class RemarkableStorageClient : IRemarkableClient
{
    private const string UploadUrl = "https://internal.cloud.remarkable.com/doc/v2/files";

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
        var meta = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { file_name = title })));

        using var request = new HttpRequestMessage(HttpMethod.Post, UploadUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
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
}
