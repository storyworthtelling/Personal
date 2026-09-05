namespace Storyworthtelling.Voice;

// Where the shared voice profile lives. The production store keeps it as a JSON file in the owner's
// Google Drive, so every com.storyworthtelling app reads one voice and the doc-ingesting app writes it.
// An app with no store (or a load failure) simply behaves as if the profile were empty.
public interface IVoiceProfileStore
{
    // Returns the current profile, or VoiceProfile.Empty when none has been saved yet.
    Task<VoiceProfile> LoadAsync(CancellationToken cancellationToken);

    // Persists the refined profile, replacing any prior one.
    Task SaveAsync(VoiceProfile profile, CancellationToken cancellationToken);
}
