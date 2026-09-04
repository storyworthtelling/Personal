using Google;
using Google.Apis.Drive.v3;
using DriveData = Google.Apis.Drive.v3.Data;

namespace Storyworthtelling.Voice;

// The Google-Drive implementation of IDriveTextFile: a single well-known file, found by name, that holds
// the shared voice profile. The DriveService is supplied by the app (each op gets a fresh, disposable one
// via the factory, the same shape the Storyboarder archive uses) so the account/token wiring stays
// app-side and this library depends only on the Drive API.
//
// The profile is ONE file shared across every com.storyworthtelling app, found by name. That requires each
// app's Drive token to carry the full `drive` scope, not the narrower `drive.file` (which would scope each
// app to its own copy) — Storyboarder's DeviceAccountTokenProvider requests it; the other apps must do the
// same, and the owner registers the scope on each OAuth client. Not yet verified against live Drive; a
// token without the scope (or any failure) just degrades the store to neutral copy.
public sealed class DriveTextFile : IDriveTextFile
{
    private readonly Func<CancellationToken, Task<DriveService>> serviceFactory;
    private readonly string fileName;

    // Optional stable-id cache (thread #13): resolve the file by its remembered Drive id first, so the owner
    // can MOVE it in Drive without breaking the app. Null keeps the original name-only behaviour.
    private readonly IDriveFileIdStore? fileIdStore;

    public DriveTextFile(
        Func<CancellationToken, Task<DriveService>> serviceFactory,
        string fileName,
        IDriveFileIdStore? fileIdStore = null)
    {
        this.serviceFactory = serviceFactory;
        this.fileName = fileName;
        this.fileIdStore = fileIdStore;
    }

    public async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        using var service = await serviceFactory(cancellationToken);
        var id = await FindId(service, cancellationToken);
        if (id is null)
            return null;

        using var stream = new MemoryStream();
        await service.Files.Get(id).DownloadAsync(stream, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public async Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        using var service = await serviceFactory(cancellationToken);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        var id = await FindId(service, cancellationToken);

        if (id is null)
        {
            var create = service.Files.Create(new DriveData.File { Name = fileName }, stream, "application/json");
            create.Fields = "id";
            var result = await create.UploadAsync(cancellationToken);
            if (result.Exception is not null)
                throw result.Exception;
            // Remember the id of the file we just created, RIGHT NOW — not only on a later name-search.
            // Without this, a file created this session but renamed/moved by the owner before any later
            // resolve was re-created as a duplicate (the id was never captured, so the next run fell back
            // to a name search that the rename defeats). Capturing on create closes that window.
            var createdId = create.ResponseBody?.Id;
            if (createdId is not null)
                fileIdStore?.Set(fileName, createdId);
            return;
        }

        // Bare metadata on update — naming anything else (like parents) is read as a move and rejected.
        var update = service.Files.Update(new DriveData.File(), id, stream, "application/json");
        var updated = await update.UploadAsync(cancellationToken);
        if (updated.Exception is not null)
            throw updated.Exception;
    }

    private async Task<string?> FindId(DriveService service, CancellationToken cancellationToken)
    {
        // 1) Prefer a remembered id, so a file the owner moved is still found where it now lives.
        var remembered = fileIdStore?.Get(fileName);
        if (remembered is not null && await StillExists(service, remembered, cancellationToken))
            return remembered;

        // 2) Fall back to the well-known-name search (the original behaviour).
        var list = service.Files.List();
        list.Q = $"name='{fileName}' and trashed=false";
        list.Fields = "files(id)";
        list.PageSize = 1;
        var found = await list.ExecuteAsync(cancellationToken);
        var id = found.Files is { Count: > 0 } ? found.Files[0].Id : null;

        // 3) Capture / re-capture the id so the next resolution skips the search and survives a move.
        if (id is not null)
            fileIdStore?.Set(fileName, id);
        return id;
    }

    // A remembered id is only usable if the file still exists and isn't trashed; otherwise the caller
    // re-resolves by name (self-healing). A 404 means the id is gone — treat it as "resolve by name".
    private static async Task<bool> StillExists(DriveService service, string id, CancellationToken cancellationToken)
    {
        try
        {
            var get = service.Files.Get(id);
            get.Fields = "id,trashed";
            var file = await get.ExecuteAsync(cancellationToken);
            return file is not null && file.Trashed != true;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
