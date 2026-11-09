using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Services;
using Codex.Infrastructure.Music;
using Storyworthtelling.Voice;

namespace Score.Google;

/// <summary>
/// Lists the owner's Drive: sub-folders (for choosing the music folder) and all
/// audio files under a folder, recursively. Uses the device-account token and the
/// AndroidMessageHandler transport, like Storyboarder's DriveDocsLister.
/// </summary>
public sealed class DriveMusicClient
{
    private const string FolderMime = DriveQuery.FolderMimeType;

    private readonly DeviceAccountTokenProvider tokens;

    public DriveMusicClient(DeviceAccountTokenProvider tokens) => this.tokens = tokens;

    public sealed record DriveFolder(string Id, string Name);

    private async Task<DriveService> CreateServiceAsync()
    {
        var token = await tokens.GetAccessToken();
        return Storyworthtelling.Google.Android.GoogleDrive.Service(token, "FocusPhone Audio");
    }

    /// <summary>Sub-folders directly inside <paramref name="folderId"/> ("root" = My Drive), for the picker.</summary>
    public async Task<IReadOnlyList<DriveFolder>> ListFoldersAsync(string folderId, CancellationToken cancellationToken)
    {
        using var service = await CreateServiceAsync();
        // Every page: a folder with more than one page of sub-folders used to lose the rest without a word.
        var files = await DrivePaging.ListFilesAsync(
            service, DriveQuery.And(DriveQuery.InFolder(folderId), DriveQuery.FoldersOnly), "id,name", 200,
            cancellationToken, orderBy: "name");
        return files
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
            var children = await DrivePaging.ListFilesAsync(
                service, DriveQuery.InFolder(folderId), "id,name,mimeType,size,modifiedTime,parents", 1000,
                cancellationToken);
            foreach (var file in children)
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
        }

        return found;
    }

    private static DateTimeOffset ParseTime(string? raw) =>
        DateTimeOffset.TryParse(raw, out var parsed) ? parsed.ToUniversalTime() : DateTimeOffset.MinValue;
}
