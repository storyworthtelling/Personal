using Google.Apis.Drive.v3;
using DriveData = Google.Apis.Drive.v3.Data;

namespace Storyworthtelling.Voice;

// The Google-Drive implementation of IDriveTextFile: a single well-known file, found by name, that holds
// the shared voice profile. The DriveService is supplied by the app (each op gets a fresh, disposable one
// via the factory, the same shape the Storyboarder archive uses) so the account/token wiring stays
// app-side and this library depends only on the Drive API.
//
// NOT yet verified against live Drive. One thing must be settled before enabling it (see the voice
// section of BATCHING.md / the wiring notes): the com.storyworthtelling apps request the drive.file scope,
// which only sees files the app itself created or opened — so for every app to read ONE shared profile the
// file has to be reachable under that scope in each app (a shared folder the user grants, or a broader
// scope). Until that's settled a single app can still read and write its own copy; the store degrades to
// neutral copy when it can't.
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
