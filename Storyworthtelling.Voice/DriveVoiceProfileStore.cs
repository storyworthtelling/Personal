using System.Text.Json;

namespace Storyworthtelling.Voice;

// The production voice-profile store: one JSON file in the owner's Google Drive, read by every
// com.storyworthtelling app and written by whichever app ingested a document. All the Drive mechanics
// live behind IDriveTextFile; this class only owns the JSON, and treats an absent, blank, or garbled file
// as "no voice learned yet" (VoiceProfile.Empty) so a first run — or a corrupt file — degrades quietly to
// neutral copy rather than throwing into the UI.
public sealed class DriveVoiceProfileStore : IVoiceProfileStore
{
    // The shared file's name. One voice, one file, referenced by name across every app.
    public const string FileName = "storyworthtelling-voice.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IDriveTextFile file;

    public DriveVoiceProfileStore(IDriveTextFile file)
    {
        this.file = file;
    }

    public async Task<VoiceProfile> LoadAsync(CancellationToken cancellationToken)
    {
        var content = await file.ReadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            return VoiceProfile.Empty;

        try
        {
            var profile = JsonSerializer.Deserialize<VoiceProfile>(content, SerializerOptions);
            return profile ?? VoiceProfile.Empty;
        }
        catch (JsonException)
        {
            // A hand-mangled or partially-written file shouldn't strand every app on an exception; behave
            // as if no voice were saved and let the next successful refine overwrite it.
            return VoiceProfile.Empty;
        }
    }

    public Task SaveAsync(VoiceProfile profile, CancellationToken cancellationToken) =>
        file.WriteAsync(JsonSerializer.Serialize(profile, SerializerOptions), cancellationToken);
}
