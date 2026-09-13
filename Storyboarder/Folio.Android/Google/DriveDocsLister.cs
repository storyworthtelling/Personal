using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Services;

namespace Folio.Android.Google;

// Browses the signed-in user's Google Drive by folder so the New Project page can navigate the folder
// tree (rather than showing every Doc in one flat list). Uses the device-account access token; the
// transport goes through AndroidMessageHandler.
public sealed class DriveDocsLister
{
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string DocMime = "application/vnd.google-apps.document";
    private const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // A child of a Drive folder: either a sub-folder (navigate into) or a document (selectable) — a
    // native Google Doc or an uploaded .docx (the owner asked for those two source kinds, nothing else).
    // GoogleDocsClient reads the file's type itself when fetching, so the entry needn't carry it.
    public sealed record DriveEntry(string Id, string Name, bool IsFolder)
    {
        // Shown in the list; the glyph distinguishes folders from docs without a custom cell template.
        public string Display => (IsFolder ? "\U0001F4C1  " : "\U0001F4C4  ") + Name;
    }

    // Lists the folders and documents (Google Docs and .docx files) directly inside the given folder
    // ("root" = My Drive root), folders first then by name.
    public async Task<IReadOnlyList<DriveEntry>> ListChildrenAsync(
        string accessToken, string folderId, CancellationToken cancellationToken)
    {
        var credential = GoogleCredential.FromAccessToken(accessToken);
        using var service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Folio",
            HttpClientFactory = new AndroidGoogleHttpClientFactory()
        });

        var request = service.Files.List();
        request.Q = $"'{folderId}' in parents and trashed=false and " +
                    $"(mimeType='{FolderMime}' or mimeType='{DocMime}' or mimeType='{DocxMime}')";
        request.Fields = "files(id,name,mimeType)";
        request.OrderBy = "folder,name";
        request.PageSize = 200;

        var response = await request.ExecuteAsync(cancellationToken);
        return response.Files
            .Where(f => !string.IsNullOrEmpty(f.Id))
            .Select(f => new DriveEntry(f.Id, f.Name ?? "(untitled)", f.MimeType == FolderMime))
            .ToList();
    }
}
