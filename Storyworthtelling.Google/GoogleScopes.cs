namespace Storyworthtelling.Google;

/// <summary>
/// The Google OAuth scopes the suite uses, and the helper that turns a requested set into the
/// <c>AccountManager</c> auth-token-type string ("oauth2:&lt;space-separated scopes&gt;"). The broker requests
/// <b>only</b> the scopes an app declares it needs (per-app minimal scopes) rather than one global superset,
/// so each app's consent prompt stays minimal.
/// </summary>
public static class GoogleScopes
{
    public const string Drive = "https://www.googleapis.com/auth/drive";
    public const string DriveReadonly = "https://www.googleapis.com/auth/drive.readonly";
    public const string DocumentsReadonly = "https://www.googleapis.com/auth/documents.readonly";
    public const string Calendar = "https://www.googleapis.com/auth/calendar";
    public const string Tasks = "https://www.googleapis.com/auth/tasks";

    /// <summary>
    /// Builds the AccountManager auth-token-type for a set of scopes: the "oauth2:" prefix plus the distinct
    /// scopes in a stable (sorted) order joined by spaces, so the same request always yields the same token
    /// type. Throws if no scopes are given — a token with no scope is never what a caller means.
    /// </summary>
    public static string ToTokenType(params string[] scopes)
    {
        var distinct = scopes
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        if (distinct.Length == 0)
            throw new ArgumentException("At least one scope is required to build a token type.", nameof(scopes));
        return "oauth2:" + string.Join(' ', distinct);
    }
}
