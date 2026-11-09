using Google.Apis.Drive.v3;
using DriveData = Google.Apis.Drive.v3.Data;

namespace Storyworthtelling.Voice;

/// <summary>
/// The one way a files.list <c>q</c> is written (audit 01 §17). Every value goes through <see cref="Literal"/>,
/// which escapes a backslash and a single quote the way Drive's search-terms guide asks — a book title with an
/// apostrophe used to break one query and pass straight into another unescaped.
/// </summary>
public static class DriveQuery
{
    public const string FolderMimeType = "application/vnd.google-apps.folder";

    /// <summary>A value quoted for a Drive query. The backslash is escaped first so a quote's escape is not doubled.</summary>
    public static string Literal(string value) =>
        "'" + value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("'", @"\'", StringComparison.Ordinal) + "'";

    /// <summary>Files with exactly this name, not in the trash.</summary>
    public static string Name(string name) => $"name={Literal(name)} and trashed=false";

    /// <summary>The name clause alone, for joining onto a query that already leaves the trash out.</summary>
    public static string NameIs(string name) => $"name={Literal(name)}";

    /// <summary>Direct children of a folder, not in the trash.</summary>
    public static string InFolder(string folderId) => $"{ParentIs(folderId)} and trashed=false";

    /// <summary>The parent clause alone, for joining onto a query that already leaves the trash out.</summary>
    public static string ParentIs(string folderId) => $"{Literal(folderId)} in parents";

    public static string FoldersOnly => $"mimeType={Literal(FolderMimeType)}";

    public static string MimeTypeIs(string mimeType) => $"mimeType={Literal(mimeType)}";

    public static string And(params string[] clauses) => string.Join(" and ", clauses);
}

/// <summary>One page of a listing: its items and the token for the next, null or empty at the end.</summary>
public sealed record DrivePage<T>(IList<T>? Items, string? NextPageToken);

/// <summary>
/// Reads every page of a listing (audit 01 §17). Several listings read one page and stopped — a folder with
/// more than 200 documents simply lost the rest, with nothing to say so.
/// </summary>
public static class DrivePaging
{
    public static async Task<IReadOnlyList<T>> ListAllAsync<T>(
        Func<string?, CancellationToken, Task<DrivePage<T>>> fetchPage, CancellationToken cancellationToken)
    {
        var all = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await fetchPage(token, cancellationToken);
            if (page.Items is not null)
                all.AddRange(page.Items);
            token = string.IsNullOrEmpty(page.NextPageToken) ? null : page.NextPageToken;
            if (token is not null && !seen.Add(token))
                throw new InvalidOperationException("Drive returned the same page token twice; the listing would never end.");
        }
        while (token is not null);
        return all;
    }

    /// <summary>
    /// Every file a query matches. <paramref name="fileFields"/> is the per-file mask (e.g. <c>id,name</c>);
    /// the page token is asked for here, since a mask that omits it silently ends the listing after one page.
    /// </summary>
    public static Task<IReadOnlyList<DriveData.File>> ListFilesAsync(
        DriveService service, string query, string fileFields, int pageSize, CancellationToken cancellationToken,
        string? orderBy = null) =>
        ListAllAsync<DriveData.File>(async (token, ct) =>
        {
            var request = service.Files.List();
            request.Q = query;
            request.Fields = $"nextPageToken, files({fileFields})";
            request.PageSize = pageSize;
            request.PageToken = token;
            if (orderBy is not null)
                request.OrderBy = orderBy;
            var result = await request.ExecuteAsync(ct);
            return new DrivePage<DriveData.File>(result.Files, result.NextPageToken);
        }, cancellationToken);
}
