using System.Text.Json;
using Codex.Domain.Audio;
using Storyworthtelling.Voice;

namespace Codex.Infrastructure.Podcasts;

/// <summary>
/// The Drive mirror of the podcast library (subscriptions + listened + resume
/// positions): one plaintext JSON file on the same narrow <see cref="IDriveTextFile"/>
/// seam the voice/taste stores use, so the Google glue stays thin and swappable and
/// this class only owns the JSON. An absent, blank, or garbled file reads as
/// <see langword="null"/> so a first run — or a corrupt file — is treated as "no
/// backup yet" and never throws.
/// </summary>
public sealed class DriveAudioLibraryStore
{
    /// <summary>The shared file's name. One library backup, one file, found by name.</summary>
    public const string FileName = "focusphone-audio-library.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IDriveTextFile file;

    public DriveAudioLibraryStore(IDriveTextFile file) => this.file = file;

    /// <summary>The backed-up snapshot, or null when there is none or the file is unreadable.</summary>
    public async Task<AudioLibrarySnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        var content = await file.ReadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AudioLibrarySnapshot>(content, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task SaveAsync(AudioLibrarySnapshot snapshot, CancellationToken cancellationToken) =>
        file.WriteAsync(JsonSerializer.Serialize(snapshot, SerializerOptions), cancellationToken);
}
