using System.Text.Json;

namespace Storyworthtelling.Voice;

// The shared voice credentials as ONE JSON file in the owner's Google Drive, found by name, next to the
// voice profile. Storyboarder's settings write it; every app reads it (both need the full `drive` scope,
// like the profile). An absent, blank, or garbled file reads as "no key yet" (VoiceConfig.Empty), so an
// app degrades to neutral copy rather than throwing.
public sealed class DriveVoiceConfigStore : IVoiceConfigStore
{
    // The shared file's name. One key, one file, referenced by name across every app.
    public const string FileName = "storyworthtelling-voice-config.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IDriveTextFile file;

    public DriveVoiceConfigStore(IDriveTextFile file)
    {
        this.file = file;
    }

    public async Task<VoiceConfig> LoadAsync(CancellationToken cancellationToken)
    {
        var content = await file.ReadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            return VoiceConfig.Empty;

        try
        {
            var config = JsonSerializer.Deserialize<VoiceConfig>(content, SerializerOptions);
            return config ?? VoiceConfig.Empty;
        }
        catch (JsonException)
        {
            return VoiceConfig.Empty;
        }
    }

    public Task SaveAsync(VoiceConfig config, CancellationToken cancellationToken) =>
        file.WriteAsync(JsonSerializer.Serialize(config, SerializerOptions), cancellationToken);
}
