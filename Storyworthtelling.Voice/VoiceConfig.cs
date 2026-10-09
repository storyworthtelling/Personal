namespace Storyworthtelling.Voice;

// The shared voice credentials, stored in ONE Drive file so the owner enters the Claude key once (in
// Storyboarder's settings) and every com.storyworthtelling app reads it from Drive to compose messages in
// their voice — no per-app key screens. The models default to the same tiers VoiceOptions uses, so a config
// that carries only the key still works.
public sealed record VoiceConfig(
    string ApiKey,
    string Model = "claude-sonnet-5-5",
    string FastModel = "claude-haiku-5-5")
{
    public static VoiceConfig Empty { get; } = new(string.Empty);

    // No key means nothing can be composed — the caller falls back to neutral copy.
    public bool IsEmpty => string.IsNullOrWhiteSpace(ApiKey);
}
