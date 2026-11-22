namespace Storyworthtelling.Voice.Health;

// Decides whether an HTTP failure is the credential's fault (so the settings screen should reveal the
// field) rather than a transient problem. Shared by the key-in-header/body APIs (Anthropic, Gemini, the
// NYT developer API, GitHub). Aggregators with their own error taxonomies (Plaid's error_code) classify
// separately in their own app. Pure and unit-tested so the rule is verifiable without a live call.
public static class AuthFailure
{
    public static bool IsHttpCredentialRejection(int statusCode, string? body)
    {
        // 401 Unauthorized / 403 Forbidden are unambiguous credential rejections everywhere.
        if (statusCode is 401 or 403)
            return true;

        // Google's Generative Language API reports an invalid key as HTTP 400 with an API_KEY_INVALID
        // marker in the body rather than a 401, so match that shape specifically.
        if (statusCode == 400 && body is not null &&
            (body.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("API key not valid", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    /// <summary>
    /// Whether anything in <paramref name="failure"/>'s chain is an HTTP credential rejection: an HttpClient status or the
    /// Google client's own. A failure with no status at all (no account signed in, no network) is not one.
    /// </summary>
    public static bool IsCredentialRejection(Exception failure)
    {
        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            var rejected = current switch
            {
                HttpRequestException { StatusCode: { } status } => IsHttpCredentialRejection((int)status, null),
                Google.GoogleApiException google => IsHttpCredentialRejection((int)google.HttpStatusCode, google.Message),
                _ => false,
            };
            if (rejected)
            {
                return true;
            }
        }

        return false;
    }
}
