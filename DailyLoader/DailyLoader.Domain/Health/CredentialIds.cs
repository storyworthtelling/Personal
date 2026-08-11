namespace DailyLoader.Domain.Health;

// Stable identifiers for the credentials whose health DailyLoader tracks, shared between the API clients
// that report it (Infrastructure) and the Settings page that reads it (the Android head) so the two never
// drift on a string literal.
public static class CredentialIds
{
    public const string NytApiKey = "nyt-api-key";
    public const string NytCookie = "nyt-cookie";
    // Must equal Storyworthtelling.Remarkable.RemarkableCredentialId.Value — the shared library's client
    // reports this credential's health, and the settings screen reads it back by this id.
    public const string Remarkable = "remarkable";
    public const string Anthropic = "anthropic";
}
