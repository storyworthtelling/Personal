using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Services;
using Codex.Infrastructure.Music;

namespace FocusPhone.Audio.Google;

/// <summary>
/// Lists the owner's Drive: sub-folders (for choosing the music folder) and all
/// audio files under a folder, recursively. Uses the device-account token and the
/// AndroidMessageHandler transport, like Storyboarder's DriveDocsLister.
/// </summary>
public sealed class DriveMusicClient
{
    private const string FolderMime = "application/vnd.google-apps.folder";

    private readonly DeviceAccountTokenProvider tokens;

    public DriveMusicClient(DeviceAccountTokenProvider tokens) => this.tokens = tokens;

    public sealed record DriveFolder(string Id, string Name);

    private async Task<DriveService> CreateServiceAsync()
    {
        var token = await tokens.GetAccessToken();
        var credential = GoogleCredential.FromAccessToken(token);
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "FocusPhone Audio",
            HttpClientFactory = new AndroidGoogleHttpClientFactory(),
        });
    }

    /// <summary>Sub-folders directly inside <paramref name="folderId"/> ("root" = My Drive), for the picker.</summary>
    public async Task<IReadOnlyList<DriveFolder>> ListFoldersAsync(string folderId, CancellationToken cancellationToken)
    {
        using var service = await CreateServiceAsync();
        var request = service.Files.List();
        request.Q = $"'{folderId}' in parents and trashed=false and mimeType='{FolderMime}'";
        request.Fields = "files(id,name)";
        request.OrderBy = "name";
        request.PageSize = 200;

        var response = await request.ExecuteAsync(cancellationToken);
        return response.Files
            .Where(file => !string.IsNullOrEmpty(file.Id))
            .Select(file => new DriveFolder(file.Id, file.Name ?? "(untitled)"))
            .ToList();
    }

    /// <summary>Every audio file under <paramref name="rootFolderId"/>, walking sub-folders breadth-first.</summary>
    public async Task<IReadOnlyList<DriveAudioFile>> ListAudioRecursiveAsync(
        string rootFolderId, CancellationToken cancellationToken)
    {
        using var service = await CreateServiceAsync();
        var found = new List<DriveAudioFile>();
        var folders = new Queue<string>();
        folders.Enqueue(rootFolderId);

        while (folders.Count > 0)
        {
            var folderId = folders.Dequeue();
            string? pageToken = null;
            do
            {
                var request = service.Files.List();
                request.Q = $"'{folderId}' in parents and trashed=false";
                request.Fields = "nextPageToken, files(id,name,mimeType,size,modifiedTime,parents)";
                request.PageSize = 1000;
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(cancellationToken);
                foreach (var file in response.Files)
                {
                    if (string.IsNullOrEmpty(file.Id))
                    {
                        continue;
                    }

                    if (file.MimeType == FolderMime)
                    {
                        folders.Enqueue(file.Id);
                        continue;
                    }

                    var name = file.Name ?? string.Empty;
                    if (AudioFileTypes.IsAudio(name, file.MimeType))
                    {
                        found.Add(new DriveAudioFile(
                            file.Id, name, file.MimeType ?? string.Empty,
                            file.Size ?? 0, ParseTime(file.ModifiedTimeRaw),
                            file.Parents?.ToList() ?? []));
                    }
                }

                pageToken = response.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));
        }

        return found;
    }

    private static DateTimeOffset ParseTime(string? raw) =>
        DateTimeOffset.TryParse(raw, out var parsed) ? parsed.ToUniversalTime() : DateTimeOffset.MinValue;
}
