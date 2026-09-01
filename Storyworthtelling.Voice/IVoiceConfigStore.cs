namespace Storyworthtelling.Voice;

// Where the shared voice credentials live. The production store keeps them in a JSON file in the owner's
// Google Drive, next to the voice profile, so the Claude key is entered once and every app reads it.
public interface IVoiceConfigStore
{
    // Returns the current config, or VoiceConfig.Empty when none has been saved yet.
    Task<VoiceConfig> LoadAsync(CancellationToken cancellationToken);

    // Persists the config, replacing any prior one.
    Task SaveAsync(VoiceConfig config, CancellationToken cancellationToken);
}
