using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Services;

namespace Storyboarder.Android.Google;

// Lists the signed-in user's Google Docs via the Drive API so the New Project page can show a
// picker. Uses the device-account access token; the transport goes through AndroidMessageHandler.
public sealed class DriveDocsLister
{
    public sealed record DocItem(string FileId, string Name);

    public async Task<IReadOnlyList<DocItem>> ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        var credential = GoogleCredential.FromAccessToken(accessToken);
        using var service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Storyboarder",
            HttpClientFactory = new AndroidGoogleHttpClientFactory()
        });

        var request = service.Files.List();
        request.Q = "mimeType='application/vnd.google-apps.document' and trashed=false";
        request.Fields = "files(id,name)";
        request.OrderBy = "modifiedTime desc";
        request.PageSize = 100;

        var response = await request.ExecuteAsync(cancellationToken);
        return response.Files
            .Where(f => !string.IsNullOrEmpty(f.Id))
            .Select(f => new DocItem(f.Id, f.Name ?? "(untitled)"))
            .ToList();
    }
}
