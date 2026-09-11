namespace Foreword.Domain.Health;

// Stable identifiers for the credentials whose health Foreword tracks, shared between the API clients
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

    // One id for the whole Maps Platform key, because it IS one key: routes, weather, pollen and
    // air quality are the same credential with different APIs enabled on it, so a rejection from
    // any of them is the same rejection.
    public const string Maps = "maps";
}
