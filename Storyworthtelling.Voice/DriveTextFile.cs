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

    public DriveTextFile(Func<CancellationToken, Task<DriveService>> serviceFactory, string fileName)
    {
        this.serviceFactory = serviceFactory;
        this.fileName = fileName;
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
        var list = service.Files.List();
        list.Q = $"name='{fileName}' and trashed=false";
        list.Fields = "files(id)";
        list.PageSize = 1;
        var found = await list.ExecuteAsync(cancellationToken);
        return found.Files is { Count: > 0 } ? found.Files[0].Id : null;
    }
}
