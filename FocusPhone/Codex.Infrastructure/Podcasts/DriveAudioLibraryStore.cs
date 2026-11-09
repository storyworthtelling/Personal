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
/// <summary>What reading the backup came to: readable (with or without a library), or a damaged file.</summary>
public sealed record AudioLibraryRead(bool Readable, AudioLibrarySnapshot? Snapshot);

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

    /// <summary>
    /// The backup, told apart from no backup and from a damaged one. A damaged file must not be backed up over,
    /// since it may hold the only copy of the library (G14). Drive being unreachable throws, as the seam does.
    /// </summary>
    public async Task<AudioLibraryRead> ReadAsync(CancellationToken cancellationToken)
    {
        var content = await file.ReadAsync(cancellationToken);
        if (content is null)
        {
            return new AudioLibraryRead(true, null);
        }

        try
        {
            var snapshot = string.IsNullOrWhiteSpace(content) ? null : JsonSerializer.Deserialize<AudioLibrarySnapshot>(content, SerializerOptions);
            return snapshot is null ? new AudioLibraryRead(false, null) : new AudioLibraryRead(true, snapshot);
        }
        catch (JsonException)
        {
            return new AudioLibraryRead(false, null);
        }
    }

    public Task SaveAsync(AudioLibrarySnapshot snapshot, CancellationToken cancellationToken) =>
        file.WriteAsync(JsonSerializer.Serialize(snapshot, SerializerOptions), cancellationToken);
}
