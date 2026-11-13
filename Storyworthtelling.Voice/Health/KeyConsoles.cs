namespace Storyworthtelling.Voice.Health;

/// <summary>Where the owner goes to make a key: the credential it is for, the link's words, and the page.</summary>
public sealed record KeyConsole(string CredentialId, string Label, Uri Address);

/// <summary>
/// The developer console (or account page) behind each key a settings screen asks for, keyed by the
/// credential id its health is kept under, so every app links one page per service and a moved console
/// is changed once. A credential with no such page — a cookie read out of a browser, a household login —
/// has no entry, and its field shows no link.
/// </summary>
public static class KeyConsoles
{
    /// <summary>Not a key: the Google Cloud OAuth client an app's Google sign-in is registered under.</summary>
    public const string GoogleOAuthClient = "google-oauth-client";

    private const string GoogleCloudCredentials = "https://console.cloud.google.com/apis/credentials";

    public static IReadOnlyList<KeyConsole> All { get; } =
    [
        Entry(SharedCredentialIds.Claude, "Get a key from the Claude Console", "https://platform.claude.com/settings/keys"),
        Entry(SharedCredentialIds.Gemini, "Get a key from Google AI Studio", "https://aistudio.google.com/app/apikey"),
        Entry("maps", "Open Google Maps Platform credentials", "https://console.cloud.google.com/google/maps-apis/credentials"),
        Entry("youtube", "Get a YouTube Data key from Google Cloud", GoogleCloudCredentials),
        Entry(GoogleOAuthClient, "Open Google Cloud credentials", GoogleCloudCredentials),
        Entry("nyt-api-key", "Get a key from NYT Developers", "https://developer.nytimes.com/my-apps"),
        Entry("plaid", "Get keys from the Plaid Dashboard", "https://dashboard.plaid.com/developers/keys"),
        Entry("streaming-availability", "Get a key from Movie of the Night", "https://developers.movieofthenight.com/"),
        Entry("omdb", "Get a key from OMDb", "https://www.omdbapi.com/apikey.aspx"),
        Entry(SharedCredentialIds.GitHubUpdateToken, "Make a token on GitHub", "https://github.com/settings/personal-access-tokens/new"),
        Entry("gmail-app-password", "Make an app password in your Google Account", "https://myaccount.google.com/apppasswords"),
        Entry("tidal", "Open the TIDAL developer dashboard", "https://developer.tidal.com/dashboard"),
        Entry(SharedCredentialIds.Remarkable, "Get a code from my.remarkable.com", "https://my.remarkable.com/device/mobile/connect"),
    ];

    /// <summary>The console for a credential, or null when it has none.</summary>
    public static KeyConsole? For(string credentialId) =>
        All.FirstOrDefault(console => string.Equals(console.CredentialId, credentialId, StringComparison.Ordinal));

    private static KeyConsole Entry(string credentialId, string label, string address) =>
        new(credentialId, label, new Uri(address, UriKind.Absolute));
}
