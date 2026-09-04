namespace Storyboarder.Domain.Health;

// Stable identifiers for the credentials whose health Storyboarder tracks. Shared between the API clients
// that report health (Infrastructure) and the settings screen that reads it (the Android head), so the
// two never drift on a string literal.
public static class CredentialIds
{
    public const string Claude = "claude";
    public const string Gemini = "gemini";
}
