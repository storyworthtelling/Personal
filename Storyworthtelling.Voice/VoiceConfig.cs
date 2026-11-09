namespace Storyworthtelling.Voice;

// The shared voice credentials, stored in ONE Drive file so the owner enters the Claude key once (in
// Storyboarder's settings) and every com.storyworthtelling app reads it from Drive to compose messages in
// their voice — no per-app key screens. The models default to the same tiers VoiceOptions uses, so a config
// that carries only the key still works.
public sealed record VoiceConfig(
    string ApiKey,
    string Model = Claude.ClaudeModels.Standard,
    string FastModel = Claude.ClaudeModels.Fast)
{
    public static VoiceConfig Empty { get; } = new(string.Empty);

    // No key means nothing can be composed — the caller falls back to neutral copy.
    public bool IsEmpty => string.IsNullOrWhiteSpace(ApiKey);
}
