using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Services;
using Storyworthtelling.Paper;
using DriveFile = global::Google.Apis.Drive.v3.Data.File;

namespace Storyboarder.Android.Google;

// Uploads a finished book's PDFs to Google Drive using the device-account access token, the same way
// DriveDocsLister reads it. Deliberately thin: it holds no decisions about what to archive or when — the
// pipeline stage owns all of that, so the parts worth testing live off-device.
//
// Requires the drive.file WRITE scope (see DeviceAccountTokenProvider.TokenScope). Until that scope is
// registered on the OAuth consent screen and granted, archiving stays switched off and every upload here
// would fail with a permission error.
public sealed class DeviceAccountDriveArchive(DeviceAccountTokenProvider tokens) : IPaperDriveArchive
{
    private const string FolderMime = "application/vnd.google-apps.folder";

    public async Task<string?> FindParentFolder(string fileId, CancellationToken cancellationToken)
    {
        using var service = await Service();
        var request = service.Files.Get(fileId);
        request.Fields = "parents";
        var file = await request.ExecuteAsync(cancellationToken);
        // A file can technically have several parents; the first is where it lives for our purposes. None
        // at all means a shared file or a shared-drive root, which the caller treats as "don't archive".
        return file.Parents is { Count: > 0 } ? file.Parents[0] : null;
    }

    public async Task<string> EnsureFolder(string parentFolderId, string name, CancellationToken cancellationToken)
    {
        using var service = await Service();

        // Reuse an existing folder of this name so a re-archive (or a resumed one) doesn't pile up
        // duplicates next to the manuscript. Escaped because a book title may well contain an apostrophe.
        var list = service.Files.List();
        list.Q = $"'{parentFolderId}' in parents and trashed=false and " +
                 $"mimeType='{FolderMime}' and name='{Escape(name)}'";
        list.Fields = "files(id)";
        list.PageSize = 1;
        var existing = await list.ExecuteAsync(cancellationToken);
        if (existing.Files is { Count: > 0 })
            return existing.Files[0].Id;

        var create = service.Files.Create(new DriveFile
        {
            Name = name,
            MimeType = FolderMime,
            Parents = [parentFolderId]
        });
        create.Fields = "id";
        var created = await create.ExecuteAsync(cancellationToken);
        return created.Id;
    }

    public async Task<string> UploadFile(
        string folderId, string name, string mimeType, byte[] content, CancellationToken cancellationToken)
    {
        using var service = await Service();

        // Overwrite rather than duplicate: a retried upload must replace its own partial, not leave two
        // copies of the same volume in the folder.
        var list = service.Files.List();
        list.Q = $"'{folderId}' in parents and trashed=false and name='{Escape(name)}'";
        list.Fields = "files(id)";
        list.PageSize = 1;
        var existing = await list.ExecuteAsync(cancellationToken);

        using var stream = new MemoryStream(content);
        if (existing.Files is { Count: > 0 })
        {
            var id = existing.Files[0].Id;
            // Metadata must be a bare object on update: naming the parents again is rejected as an
            // attempted move rather than treated as a no-op.
            var update = service.Files.Update(new DriveFile(), id, stream, mimeType);
            update.Fields = "id";
            var updated = await update.UploadAsync(cancellationToken);
            if (updated.Exception is not null)
                throw updated.Exception;
            return id;
        }

        var upload = service.Files.Create(
            new DriveFile { Name = name, Parents = [folderId] }, stream, mimeType);
        upload.Fields = "id";
        var result = await upload.UploadAsync(cancellationToken);
        if (result.Exception is not null)
            throw result.Exception;
        return upload.ResponseBody?.Id
            ?? throw new InvalidOperationException($"Drive accepted the upload of '{name}' but returned no file id.");
    }

    private async Task<DriveService> Service()
    {
        var token = await tokens.GetAccessToken();
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(token),
            ApplicationName = "Storyboarder",
            HttpClientFactory = new AndroidGoogleHttpClientFactory()
        });
    }

    // Drive query strings are single-quoted, so an apostrophe in a book title would otherwise terminate
    // the literal and produce a malformed query.
    private static string Escape(string value) => value.Replace("'", @"\'");
}
