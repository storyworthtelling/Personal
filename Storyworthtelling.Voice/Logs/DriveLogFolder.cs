using System.Text;
using System.Text.Json;
using Google;
using Google.Apis.Drive.v3;
using DriveData = Google.Apis.Drive.v3.Data;

namespace Storyworthtelling.Voice.Logs;

/// <summary>
/// The Google Drive implementation of <see cref="IDriveLogFolder"/>: the folder
/// <c>storyworthtelling-logs</c> in this account's Drive, found by name, made on the first create. Built on
/// the same per-call <see cref="DriveService"/> factory shape as <see cref="DriveTextFile"/>, so the account
/// and token wiring stay in the head. Every uploading head already holds a full-<c>drive</c> token for the
/// vault, so no new scope is needed.
/// <para>
/// A name inside the folder is resolved by <see cref="DriveFileChooser"/> like every other well-known name
/// (owned before shared, then most recent), so two copies never make the answer depend on listing order.
/// Nothing here deletes or overwrites: the only write after a create is setting <c>trashed</c> on a file
/// this account owns (A1: 30 days to recover from a bad triage).
/// </para>
/// </summary>
public sealed class DriveLogFolder : IDriveLogFolder
{
    /// <summary>The names-only list the reader writes for a phone whose files it cannot trash (§5.6).</summary>
    public const string ProcessedListName = "swt-log-processed.json";

    private const string FolderMimeType = "application/vnd.google-apps.folder";

    private readonly Func<CancellationToken, Task<DriveService>> serviceFactory;
    private readonly string folderName;

    public DriveLogFolder(Func<CancellationToken, Task<DriveService>> serviceFactory, string folderName = Storyworthtelling.Diagnostics.LogUploadPlan.FolderName)
    {
        this.serviceFactory = serviceFactory;
        this.folderName = folderName;
    }

    public async Task<string?> FindAsync(string name, CancellationToken cancellationToken)
    {
        using var service = await serviceFactory(cancellationToken);
        var folderId = await FolderId(service, create: false, cancellationToken);
        return folderId is null ? null : (await Choose(service, folderId, name, cancellationToken)).Id;
    }

    public async Task<string> CreateAsync(string name, string content, CancellationToken cancellationToken)
    {
        using var service = await serviceFactory(cancellationToken);
        var folderId = (await FolderId(service, create: true, cancellationToken))!;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var create = service.Files.Create(
            new DriveData.File { Name = name, Parents = [folderId] }, stream, "application/json");
        create.Fields = "id";
        var progress = await create.UploadAsync(cancellationToken);
        if (progress.Exception is not null)
        {
            throw new IOException($"Drive refused to create {name}.", progress.Exception);
        }
        return create.ResponseBody?.Id
            ?? (await Choose(service, folderId, name, cancellationToken)).Id
            ?? throw new IOException($"Drive created {name} but returned no id for it.");
    }

    public async Task<string?> ReadAsync(string id, CancellationToken cancellationToken)
    {
        using var service = await serviceFactory(cancellationToken);
        try
        {
            using var stream = new MemoryStream();
            var progress = await service.Files.Get(id).DownloadAsync(stream, cancellationToken);
            if (progress.Exception is GoogleApiException { HttpStatusCode: System.Net.HttpStatusCode.NotFound })
            {
                return null;
            }
            if (progress.Exception is not null)
            {
                throw new IOException($"Drive could not read back file {id}.", progress.Exception);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (GoogleApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> ProcessedNamesAsync(CancellationToken cancellationToken)
    {
        var id = await FindAsync(ProcessedListName, cancellationToken);
        if (id is null)
        {
            return [];
        }
        var content = await ReadAsync(id, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }
        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("names", out var names) || names.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            return names.EnumerateArray()
                .Where(n => n.ValueKind == JsonValueKind.String)
                .Select(n => n.GetString()!)
                .ToList();
        }
        catch (JsonException)
        {
            // An unreadable list processes nothing; the files simply wait another week.
            return [];
        }
    }

    public async Task TrashOwnAsync(string name, CancellationToken cancellationToken)
    {
        using var service = await serviceFactory(cancellationToken);
        var folderId = await FolderId(service, create: false, cancellationToken);
        if (folderId is null)
        {
            return;
        }
        var choice = await Choose(service, folderId, name, cancellationToken);
        if (choice.Id is null || choice.Ownership != DriveFileOwnership.Owned)
        {
            return;
        }
        await service.Files.Update(new DriveData.File { Trashed = true }, choice.Id).ExecuteAsync(cancellationToken);
    }

    private async Task<string?> FolderId(DriveService service, bool create, CancellationToken cancellationToken)
    {
        var list = service.Files.List();
        list.Q = $"{DriveFileChooser.NameQuery(folderName)} and mimeType='{FolderMimeType}'";
        list.Fields = DriveFileChooser.ListFields;
        list.PageSize = DriveFileChooser.ListPageSize;
        var found = await list.ExecuteAsync(cancellationToken);
        var id = DriveFileChooser.Choose(DriveFileChooser.FromDrive(found.Files)).Id;
        if (id is not null || !create)
        {
            return id;
        }

        var make = service.Files.Create(new DriveData.File { Name = folderName, MimeType = FolderMimeType });
        make.Fields = "id";
        var folder = await make.ExecuteAsync(cancellationToken);
        return folder.Id ?? throw new IOException($"Drive created the folder {folderName} but returned no id for it.");
    }

    private static async Task<DriveFileChoice> Choose(DriveService service, string folderId, string name, CancellationToken cancellationToken)
    {
        var list = service.Files.List();
        list.Q = $"{DriveFileChooser.NameQuery(name)} and '{folderId}' in parents";
        list.Fields = DriveFileChooser.ListFields;
        list.PageSize = DriveFileChooser.ListPageSize;
        var found = await list.ExecuteAsync(cancellationToken);
        return DriveFileChooser.Choose(DriveFileChooser.FromDrive(found.Files));
    }
}
