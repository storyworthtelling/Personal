namespace Storyworthtelling.Voice;

/// <summary>
/// The read half of the profile store: what a seat that only speaks is given (audit W10.2). Foreword's
/// prayer line reads the voice through this, so nothing it holds can write the profile back, and a journal
/// entry has no path into the file every app shares.
/// </summary>
public interface IVoiceProfileSource
{
    // Returns the current profile, or VoiceProfile.Empty when none has been saved yet.
    Task<VoiceProfile> LoadAsync(CancellationToken cancellationToken);
}

// Where the shared voice profile lives. The production store keeps it as a JSON file in the owner's
// Google Drive, so every com.storyworthtelling app reads one voice and the doc-ingesting app writes it.
// An app with no store (or a load failure) simply behaves as if the profile were empty.
public interface IVoiceProfileStore : IVoiceProfileSource
{
    // Persists the refined profile, replacing any prior one.
    Task SaveAsync(VoiceProfile profile, CancellationToken cancellationToken);
}
