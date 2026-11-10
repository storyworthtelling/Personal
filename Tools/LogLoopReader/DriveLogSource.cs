using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice;

namespace LogLoopReader;

/// <summary>
/// The log folder in the owner's Drive, read with the reader's own read-only key (O13). The key is
/// <c>SWT_DRIVE_READ_TOKEN</c>: an "authorized user" JSON — <c>client_id</c>, <c>client_secret</c>,
/// <c>refresh_token</c> — for a dedicated OAuth client granted <c>drive.readonly</c> and nothing else.
/// Exchanging a refresh token needs its client, which is why the secret carries all three.
/// </summary>
public sealed class DriveLogSource : ILogSource
{
    private readonly DriveService service;
    private readonly string folderName;

    public DriveLogSource(string authorizedUserJson, string folderName = LogUploadPlan.FolderName)
    {
        using var document = JsonDocument.Parse(authorizedUserJson);
        var root = document.RootElement;
        string Field(string name) => root.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text
            ? text
            : throw new ArgumentException($"SWT_DRIVE_READ_TOKEN has no {name}.");

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = Field("client_id"), ClientSecret = Field("client_secret") },
            Scopes = [DriveService.ScopeConstants.DriveReadonly],
        });
        var credential = new UserCredential(flow, "log-loop", new TokenResponse { RefreshToken = Field("refresh_token") });
        service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "LogLoopReader",
        });
        this.folderName = folderName;
    }

    public async Task<IReadOnlyList<RemoteLogFile>> ListAsync(CancellationToken cancellationToken)
    {
        var folders = service.Files.List();
        folders.Q = DriveQuery.And(DriveQuery.Name(folderName), DriveQuery.FoldersOnly);
        folders.Fields = "files(id)";
        var folder = (await folders.ExecuteAsync(cancellationToken)).Files.FirstOrDefault()
            ?? throw new InvalidOperationException($"No Drive folder named {folderName} is visible to the reader's key.");

        var files = await DrivePaging.ListFilesAsync(
            service, DriveQuery.InFolder(folder.Id), "id,name", 1000, cancellationToken);
        return files.Select(f => new RemoteLogFile(f.Id, f.Name)).ToList();
    }

    public async Task<string> DownloadAsync(string id, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var progress = await service.Files.Get(id).DownloadAsync(stream, cancellationToken);
        if (progress.Exception is not null)
        {
            throw new IOException($"Could not download log file {id}.", progress.Exception);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
