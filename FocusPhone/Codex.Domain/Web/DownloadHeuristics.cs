namespace Codex.Domain.Web;

/// <summary>
/// Recognises links that point at a file to save rather than a page to render.
/// The restricted browser confines <em>navigation</em> to a single registrable
/// domain (<see cref="LinkPolicy"/>), but a download hands a file to the system
/// download manager without opening another site, so it does not widen the
/// browsable surface. A download link is therefore allowed to cross domains — a
/// GitHub release or Actions artifact, for instance, redirects github.com to a
/// signed blob-storage host on an unrelated domain. Detection uses the server's
/// attachment intent when the URL carries a Content-Disposition hint (signed
/// storage URLs put it in the query string), otherwise the extension of the
/// effective filename.
/// </summary>
public static class DownloadHeuristics
{
    // Query keys under which signed storage URLs carry a Content-Disposition
    // override: Azure blob storage uses "rscd"; S3/GCS use the spelled-out form.
    private static readonly string[] DispositionQueryKeys =
        ["rscd", "response-content-disposition", "content-disposition"];

    // Extensions that name a browsable document. A link ending in one of these is
    // a page to navigate, never a file to download.
    private static readonly HashSet<string> WebDocumentExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "html", "htm", "xhtml", "shtml", "php", "asp", "aspx", "jsp", "cgi",
        };

    /// <summary>
    /// True when <paramref name="target"/> should be saved as a file rather than
    /// opened as a page — regardless of whether it shares the origin's domain.
    /// </summary>
    public static bool IsDownloadUrl(Uri target)
    {
        if (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var disposition = ReadContentDisposition(target);
        if (disposition is not null)
        {
            if (disposition.Contains("attachment", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            var dispositionName = ReadFilename(disposition);
            if (dispositionName is not null)
            {
                return HasDownloadableExtension(dispositionName);
            }
        }

        return HasDownloadableExtension(LastPathSegment(target));
    }

    /// <summary>
    /// The decoded Content-Disposition value carried in the URL's query string, or
    /// null when the URL has none. Used to recover a human filename for the saved
    /// file when the path itself is opaque (a hash), as on signed storage URLs.
    /// </summary>
    public static string? ReadContentDisposition(Uri target)
    {
        var query = target.Query;
        if (query.Length <= 1)
        {
            return null;
        }

        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var split = pair.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }
            var key = System.Net.WebUtility.UrlDecode(pair[..split]);
            foreach (var dispositionKey in DispositionQueryKeys)
            {
                if (string.Equals(key, dispositionKey, StringComparison.OrdinalIgnoreCase))
                {
                    return System.Net.WebUtility.UrlDecode(pair[(split + 1)..]);
                }
            }
        }
        return null;
    }

    private static string? ReadFilename(string contentDisposition)
    {
        const string marker = "filename=";
        var at = contentDisposition.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }
        var value = contentDisposition[(at + marker.Length)..].Trim().Trim('"');
        return value.Length == 0 ? null : value;
    }

    private static string LastPathSegment(Uri target)
    {
        var path = target.AbsolutePath;
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private static bool HasDownloadableExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        if (dot < 0 || dot == fileName.Length - 1)
        {
            return false;
        }
        var extension = fileName[(dot + 1)..];
        if (extension.Length > 8 || !extension.Any(char.IsAsciiLetter)
            || !extension.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }
        return !WebDocumentExtensions.Contains(extension);
    }
}
