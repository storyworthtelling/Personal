using System.Text.Json;
using FocusPhone.Domain.Music;
using Storyworthtelling.Voice;

namespace FocusPhone.Infrastructure.Music;

/// <summary>
/// The production Taste Profile store: one JSON file in the owner's Drive, sitting on the same narrow
/// <see cref="IDriveTextFile"/> seam the Voice library uses, so the Drive glue stays thin and swappable and
/// this class only owns the JSON. An absent, blank, or garbled file reads as <see cref="TasteProfile.Empty"/>
/// so a first run — or a corrupt file — degrades quietly to "no taste learned yet" rather than throwing.
/// </summary>
public sealed class DriveTasteProfileStore : ITasteProfileStore
{
    /// <summary>The shared file's name. One profile, one file, referenced by name.</summary>
    public const string FileName = "storyworthtelling-taste.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IDriveTextFile file;

    public DriveTasteProfileStore(IDriveTextFile file) => this.file = file;

    public async Task<TasteProfile> LoadAsync(CancellationToken cancellationToken)
    {
        var content = await file.ReadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return TasteProfile.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<TasteProfile>(content, SerializerOptions) ?? TasteProfile.Empty;
        }
        catch (JsonException)
        {
            return TasteProfile.Empty;
        }
    }

    public Task SaveAsync(TasteProfile profile, CancellationToken cancellationToken) =>
        file.WriteAsync(JsonSerializer.Serialize(profile, SerializerOptions), cancellationToken);
}
