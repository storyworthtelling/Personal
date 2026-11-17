using Storyworthtelling.Google.Android;
using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Services;
using Storyworthtelling.Paper;
using Storyworthtelling.Voice;
using DriveFile = global::Google.Apis.Drive.v3.Data.File;

namespace Folio.Android.Google;

// Uploads a finished book's PDFs to Google Drive using the device-account access token, the same way
// DriveDocsLister reads it. Deliberately thin: it holds no decisions about what to archive or when — the
// pipeline stage owns all of that, so the parts worth testing live off-device.
//
// Requires the drive.file WRITE scope (see DeviceAccountTokenProvider.TokenScope). Until that scope is
// registered on the OAuth consent screen and granted, archiving stays switched off and every upload here
// would fail with a permission error.
public sealed class DeviceAccountDriveArchive(DeviceAccountTokenProvider tokens) : IPaperDriveArchive
{
    private const string FolderMime = DriveQuery.FolderMimeType;

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
        // duplicates next to the manuscript. DriveQuery escapes the title, which may well hold an apostrophe.
        var list = service.Files.List();
        list.Q = DriveQuery.And(DriveQuery.InFolder(parentFolderId), DriveQuery.FoldersOnly, DriveQuery.NameIs(name));
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
        list.Q = DriveQuery.And(DriveQuery.InFolder(folderId), DriveQuery.NameIs(name));
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

    private const string GoogleDocMime = "application/vnd.google-apps.document";

    public async Task<string> WriteGoogleDoc(
        string? fileId, string folderId, string name, string text, CancellationToken cancellationToken)
    {
        using var service = await Service();
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);

        // Rewritten in place, by id: Drive converts the text into the existing doc, and the doc's version
        // history keeps the write. A doc that has gone (deleted by hand) is made again rather than failing.
        if (!string.IsNullOrEmpty(fileId))
        {
            try
            {
                using var replacement = new MemoryStream(bytes);
                var update = service.Files.Update(new DriveFile(), fileId, replacement, "text/plain");
                update.Fields = "id";
                var updated = await update.UploadAsync(cancellationToken);
                if (updated.Exception is null)
                    return fileId;
                if (updated.Exception is not global::Google.GoogleApiException { HttpStatusCode: System.Net.HttpStatusCode.NotFound })
                    throw updated.Exception;
            }
            catch (global::Google.GoogleApiException missing) when (missing.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
            }
        }

        using var stream = new MemoryStream(bytes);
        var create = service.Files.Create(
            new DriveFile { Name = name, Parents = [folderId], MimeType = GoogleDocMime }, stream, "text/plain");
        create.Fields = "id";
        var result = await create.UploadAsync(cancellationToken);
        if (result.Exception is not null)
            throw result.Exception;
        return create.ResponseBody?.Id
            ?? throw new InvalidOperationException($"Drive accepted the Google Doc '{name}' but returned no file id.");
    }

    public async Task<string> WriteTextFile(
        string? fileId, string folderId, string name, string mimeType, string text, CancellationToken cancellationToken)
    {
        using var service = await Service();
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);

        // Rewritten in place, by id; a file deleted by hand is made again rather than failing.
        if (!string.IsNullOrEmpty(fileId))
        {
            try
            {
                using var replacement = new MemoryStream(bytes);
                var update = service.Files.Update(new DriveFile(), fileId, replacement, mimeType);
                update.Fields = "id";
                var updated = await update.UploadAsync(cancellationToken);
                if (updated.Exception is null)
                    return fileId;
                if (updated.Exception is not global::Google.GoogleApiException { HttpStatusCode: System.Net.HttpStatusCode.NotFound })
                    throw updated.Exception;
            }
            catch (global::Google.GoogleApiException missing) when (missing.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
            }
        }

        using var stream = new MemoryStream(bytes);
        var create = service.Files.Create(new DriveFile { Name = name, Parents = [folderId] }, stream, mimeType);
        create.Fields = "id";
        var result = await create.UploadAsync(cancellationToken);
        if (result.Exception is not null)
            throw result.Exception;
        return create.ResponseBody?.Id
            ?? throw new InvalidOperationException($"Drive accepted the file '{name}' but returned no file id.");
    }

    public async Task<string?> FindFileByName(string name, CancellationToken cancellationToken)
    {
        using var service = await Service();
        var list = service.Files.List();
        list.Q = DriveQuery.Name(name);
        list.Fields = "files(id, modifiedTime)";
        list.OrderBy = "modifiedTime desc";
        list.PageSize = 1;
        var found = await list.ExecuteAsync(cancellationToken);
        return found.Files is { Count: > 0 } ? found.Files[0].Id : null;
    }

    public async Task<string> ReadTextFile(string fileId, CancellationToken cancellationToken)
    {
        using var service = await Service();
        using var stream = new MemoryStream();
        var progress = await service.Files.Get(fileId).DownloadAsync(stream, cancellationToken);
        if (progress.Exception is not null)
            throw progress.Exception;
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task<DriveService> Service()
    {
        var token = await tokens.GetAccessToken();
        return GoogleDrive.Service(token, "Folio");
    }
}
