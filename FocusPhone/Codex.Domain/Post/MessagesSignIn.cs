using System.Text.Json;

namespace Codex.Domain.Post;

/// <summary>The pairing cookies read out of a messages.google.com sign-in.</summary>
public sealed record SignInCookies(IReadOnlyDictionary<string, string> Cookies, IReadOnlyList<string> Missing)
{
    public bool IsComplete => Missing.Count == 0;

    /// <summary>The JSON object (name → value) the bridge's pairing call takes.</summary>
    public string ToJson() => JsonSerializer.Serialize(Cookies);
}

/// <summary>
/// The Google sign-in that pairs Post with Google Messages.
///
/// <para>Messages for Web pairs a Google account by its sign-in cookies, so the sign-in has to
/// happen in a web view whose cookies Post can read — Post's own. Chrome, even in Flyleaf's
/// Admin mode, keeps its cookies to itself, so a sign-in there would leave Post with nothing.
/// The page starts at Google's account chooser and continues to Messages; once it lands on
/// messages.google.com the cookies are there to read.</para>
///
/// <para>Which cookies count comes from mautrix-gmessages' own login step
/// (pkg/connector/login.go): six required, one optional. OSID is set by messages.google.com
/// itself, so a sign-in that never reached Messages is missing it.</para>
///
/// <para>The page is a sign-in page, not a browser: it may go where Google's sign-in goes
/// (its account pages on google.com, the accounts.youtube.com cookie hop) and nowhere else.</para>
/// </summary>
public static class MessagesSignIn
{
    public static Uri StartUrl { get; } =
        new("https://accounts.google.com/AccountChooser?continue=https://messages.google.com/web/config");

    /// <summary>The URL whose cookies are read: it sees the .google.com cookies and Messages' own OSID.</summary>
    public const string CookieUrl = "https://messages.google.com";

    public const string MessagesHost = "messages.google.com";

    public static IReadOnlyList<string> RequiredCookies { get; } = ["SID", "HSID", "OSID", "SSID", "APISID", "SAPISID"];

    public static IReadOnlyList<string> OptionalCookies { get; } = ["__Secure-1PSIDTS"];

    /// <summary>Reads a Cookie header (what Android's CookieManager returns) down to the pairing cookies.</summary>
    public static SignInCookies Read(string? cookieHeader)
    {
        var wanted = RequiredCookies.Concat(OptionalCookies).ToHashSet(StringComparer.Ordinal);
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in (cookieHeader ?? string.Empty).Split(';'))
        {
            var equals = pair.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }
            var name = pair[..equals].Trim();
            var value = pair[(equals + 1)..].Trim();
            // The same name can come back twice; the first is the more specific cookie.
            if (value.Length > 0 && wanted.Contains(name) && !found.ContainsKey(name))
            {
                found[name] = value;
            }
        }
        // Keep the order the bridge's own login names them in, so the JSON is stable.
        var ordered = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in RequiredCookies.Concat(OptionalCookies))
        {
            if (found.TryGetValue(name, out var value))
            {
                ordered[name] = value;
            }
        }
        var missing = RequiredCookies.Where(name => !ordered.ContainsKey(name)).ToList();
        return new SignInCookies(ordered, missing);
    }

    /// <summary>True once the page has landed on Messages itself — the sign-in is over.</summary>
    public static bool HasReachedMessages(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps
        && string.Equals(url.Host, MessagesHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the sign-in page may navigate to <paramref name="url"/>.</summary>
    public static bool AllowsNavigation(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }
        var host = url.Host.ToLowerInvariant();
        return host == "google.com"
            || host.EndsWith(".google.com", StringComparison.Ordinal)
            || host == "accounts.youtube.com";
    }
}
