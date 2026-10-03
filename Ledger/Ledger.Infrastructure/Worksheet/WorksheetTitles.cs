namespace Index.Infrastructure.Worksheet;

/// <summary>
/// D60: "this month's page replaces last month's on the reMarkable — history lives in the app, not as
/// accumulated pages." A single fixed title (not one per month, unlike Foreword's dated documents) is
/// what makes "replace" a literal <c>DownloadByTitle</c> + <c>MoveToTrash</c> + <c>Upload</c> under the
/// same name, rather than a title-pattern search across many documents — there is never more than one
/// worksheet on the reMarkable at a time.
/// </summary>
public static class WorksheetTitles
{
    public const string Current = "Index Budget Worksheet";
    public const string Folder = "Index";

    // Release H (D153): the follow-up is its OWN fixed title — a separate document from the worksheet
    // itself, replaced under this same name each morning it recurs ("each new page replaces the last on
    // the reMarkable", D60's own rule applied to the follow-up).
    public const string FollowUpCurrent = "Index Budget Follow-up";
}
